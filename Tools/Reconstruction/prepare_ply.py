"""Normalize trained Gaussian PLY to the pinned UnityGaussianSplatting v1.0 layout.

The upstream v1.0 importer expects exactly 62 float32 values per vertex, in the
Graphdeco degree-3 order. Nerfstudio exports can omit normals or lower-order SH.
No coordinate transform is applied: Unity's renderer root handles (x,z,y).
"""
import argparse
from pathlib import Path
import numpy as np

SCALARS = {"float": "f4", "float32": "f4", "double": "f8", "float64": "f8",
           "uchar": "u1", "uint8": "u1", "char": "i1", "int8": "i1",
           "short": "i2", "int16": "i2", "ushort": "u2", "uint16": "u2",
           "int": "i4", "int32": "i4", "uint": "u4", "uint32": "u4"}
NAMES = (["x", "y", "z", "nx", "ny", "nz"] + [f"f_dc_{i}" for i in range(3)] +
         [f"f_rest_{i}" for i in range(45)] + ["opacity"] +
         [f"scale_{i}" for i in range(3)] + [f"rot_{i}" for i in range(4)])


def read_ply(path):
    with path.open("rb") as stream:
        if stream.readline().strip() != b"ply":
            raise ValueError("Not a PLY file")
        count, properties, fmt, current = 0, [], "", None
        for _ in range(1024):
            line = stream.readline(16385)
            if len(line) > 16384:
                raise ValueError("Excessive PLY header line")
            words = line.decode("ascii").split()
            if words == ["end_header"]:
                break
            if not words:
                raise ValueError("Incomplete PLY header")
            if words[0] == "format":
                fmt = words[1]
            elif words[0] == "element":
                if count == 0 and words[1] != "vertex" and int(words[2]) > 0:
                    raise ValueError("Vertex data must be the first populated element")
                current = words[1]
                if current == "vertex":
                    count = int(words[2])
            elif words[0] == "property" and current == "vertex":
                if words[1] not in SCALARS:
                    raise ValueError("Unsupported vertex scalar/list property: " + words[1])
                properties.append((words[2], "<" + SCALARS[words[1]]))
        else:
            raise ValueError("PLY header exceeds 1024 lines")
        if not 0 < count <= 20_000_000 or not properties or len(properties) > 256:
            raise ValueError("Invalid PLY count or properties")
        if fmt == "binary_little_endian":
            data = np.fromfile(stream, dtype=np.dtype(properties), count=count)
            if len(data) != count:
                raise ValueError("Truncated PLY vertex data")
            return {name: data[name] for name, _ in properties}
        if fmt == "ascii":
            data = np.loadtxt(stream, max_rows=count, ndmin=2, dtype=np.float32)
            if data.shape != (count, len(properties)):
                raise ValueError("Unexpected ASCII vertex layout")
            return {item[0]: data[:, index] for index, item in enumerate(properties)}
        raise ValueError("Only ASCII and binary_little_endian PLY are supported")


def normalize(source, output, max_splats=250000, min_opacity=0.02):
    source, output = Path(source), Path(output)
    if output.exists():
        raise ValueError("Output exists; choose a new path to preserve previous data")
    data = read_ply(source)
    if not all(f"f_dc_{i}" in data for i in range(3)) and all(key in data for key in ("red", "green", "blue")):
        for i, key in enumerate(("red", "green", "blue")):
            rgb = data[key].astype(np.float32)
            if data[key].dtype.kind in "iu" or np.max(rgb) > 1:
                rgb = rgb / 255
            data[f"f_dc_{i}"] = (rgb - 0.5) / 0.282094792
    required = ["x", "y", "z", "opacity"] + [f"f_dc_{i}" for i in range(3)] + \
               [f"scale_{i}" for i in range(3)] + [f"rot_{i}" for i in range(4)]
    if any(key not in data for key in required):
        raise ValueError("Expected trained Gaussian PLY (not a point cloud); missing " +
                         ", ".join(key for key in required if key not in data))
    rest = sum(key.startswith("f_rest_") for key in data)
    if rest not in (0, 9, 24, 45) or any(f"f_rest_{i}" not in data for i in range(rest)):
        raise ValueError("SH count must be 0, 9, 24 or 45 in contiguous channel-major order")
    count = len(data["x"])
    valid = np.ones(count, dtype=bool)
    for values in data.values():
        valid &= np.isfinite(values)
    # Stable sigmoid for opacity logits.
    alpha = 1 / (1 + np.exp(-np.clip(data["opacity"].astype(np.float64), -80, 80)))
    valid &= alpha >= min_opacity
    indices = np.flatnonzero(valid)
    if not len(indices):
        raise ValueError("No finite Gaussians remain after opacity filtering")
    if len(indices) > max_splats:
        indices = indices[np.linspace(0, len(indices) - 1, max_splats, dtype=np.int64)]
    packed = np.zeros((len(indices), len(NAMES)), dtype="<f4")
    for column, key in enumerate(NAMES):
        if not key.startswith("f_rest_") and key in data:
            packed[:, column] = data[key][indices]
    coeff = rest // 3
    for channel in range(3):
        for coefficient in range(coeff):
            packed[:, 9 + channel * 15 + coefficient] = data[f"f_rest_{channel*coeff+coefficient}"][indices]
    q = packed[:, -4:]
    norm = np.linalg.norm(q, axis=1)
    if np.any(norm < 1e-8):
        raise ValueError("Zero Gaussian quaternion")
    q /= norm[:, None]
    if not np.isfinite(packed).all() or np.any(packed[:, -7:-4] > np.log(10000)):
        raise ValueError("Non-finite float32 value or excessive Gaussian scale")
    header = (f"ply\nformat binary_little_endian 1.0\nelement vertex {len(indices)}\n" +
              "".join(f"property float {name}\n" for name in NAMES) + "end_header\n")
    output.parent.mkdir(parents=True, exist_ok=True)
    with output.open("xb") as stream:
        stream.write(header.encode("ascii"))
        packed.tofile(stream)
    print(f"Normalized {count:,} -> {len(indices):,} Gaussians, SH degree-3 layout, {output}")
    print("Budget filtering is deterministic sampling, not quality-aware pruning or LOD.")
    return len(indices)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--input", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--max-splats", type=int, default=250000)
    parser.add_argument("--min-opacity", type=float, default=0.02)
    args = parser.parse_args()
    if args.max_splats <= 0 or not 0 <= args.min_opacity <= 1:
        parser.error("Budget must be positive and min-opacity must be 0..1")
    normalize(args.input, args.output, args.max_splats, args.min_opacity)


if __name__ == "__main__":
    main()
