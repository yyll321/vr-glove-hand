"""Bake Nerfstudio Nerfacto into NRV1: Unity Y-up bounds + z/y/x RGBA float16.

RGB is averaged over six fixed view directions. Alpha stores density per training
unit, NOT opacity. Baking deliberately trades view dependence for Unity VR speed.
"""
import argparse
from pathlib import Path
import struct
import numpy as np


def bake(args):
    import torch
    from nerfstudio.cameras.rays import Frustums, RaySamples
    from nerfstudio.field_components.field_heads import FieldHeadNames
    from nerfstudio.utils.eval_utils import eval_setup

    if args.output.exists():
        raise ValueError("Output already exists. Choose a new path.")
    _, pipeline, _, _ = eval_setup(args.config, test_mode="inference")
    model = pipeline.model
    if not hasattr(model, "field"):
        raise ValueError("This baker requires a Nerfacto-compatible field, not a Gaussian checkpoint.")
    model.eval()
    device = pipeline.device
    n = args.resolution
    # Arguments are in training Z-up space. Output coordinates use Unity (x,z,y).
    source_lo = np.asarray(args.bounds[:3], dtype=np.float32)
    source_hi = np.asarray(args.bounds[3:], dtype=np.float32)
    if not np.isfinite(source_lo).all() or not np.isfinite(source_hi).all() or np.any(source_hi <= source_lo):
        raise ValueError("Bounds must be finite and each maximum must exceed its minimum.")
    lo, hi = source_lo[[0, 2, 1]], source_hi[[0, 2, 1]]
    axes = [torch.linspace(float(lo[i] + (hi[i] - lo[i]) / n / 2),
                           float(hi[i] - (hi[i] - lo[i]) / n / 2), n, device=device) for i in range(3)]
    directions = torch.tensor([[1, 0, 0], [-1, 0, 0], [0, 1, 0], [0, -1, 0], [0, 0, 1], [0, 0, -1]],
                              dtype=torch.float32, device=device)
    args.output.parent.mkdir(parents=True, exist_ok=True)
    temporary = args.output.with_suffix(args.output.suffix + ".partial")
    if temporary.exists():
        raise ValueError(f"Partial export already exists: {temporary}; inspect it or choose another output.")
    with temporary.open("xb") as out, torch.no_grad():
        out.write(struct.pack("<4sI6f", b"NRV1", n, *lo.tolist(), *hi.tolist()))
        # Iterate in flat Unity Texture3D order: x fastest, then y, then z.
        total = n ** 3
        for start in range(0, total, args.chunk):
            indices = torch.arange(start, min(start + args.chunk, total), device=device)
            unity = torch.stack((axes[0][indices % n], axes[1][(indices // n) % n], axes[2][indices // (n*n)]), dim=-1)
            positions = unity[:, [0, 2, 1]]
            count = len(positions)
            zeros = torch.zeros((count, 1), device=device)
            rgb = torch.zeros((count, 3), device=device)
            density = None
            for direction in directions:
                rays = RaySamples(frustums=Frustums(origins=positions, directions=direction.expand_as(positions),
                                                   starts=zeros, ends=zeros, pixel_area=torch.ones_like(zeros)),
                                  camera_indices=torch.zeros((count, 1), device=device, dtype=torch.long))
                result = model.field(rays)
                rgb += result[FieldHeadNames.RGB].float() / len(directions)
                if density is None:
                    density = result[FieldHeadNames.DENSITY].float()
            packed = torch.cat((rgb, density), dim=-1)
            if not torch.isfinite(packed).all():
                raise ValueError("Field returned NaN or infinity; reduce crop bounds or inspect checkpoint.")
            packed = packed.clamp(0, 65000).cpu().numpy().astype("<f2")
            out.write(packed.tobytes(order="C"))
            print(f"Baked {min(start + args.chunk, total):,}/{total:,} voxels", flush=True)
    temporary.rename(args.output)
    print(f"Saved {args.output} ({args.output.stat().st_size / 1024**2:.1f} MiB). Import through Tools/Reconstruction.")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--config", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--resolution", type=int, choices=[32, 64, 128, 256], default=128)
    parser.add_argument("--chunk", type=int, default=32768)
    parser.add_argument("--bounds", type=float, nargs=6, required=True,
                        metavar=("XMIN", "YMIN", "ZMIN", "XMAX", "YMAX", "ZMAX"),
                        help="Crop in Nerfstudio training world Z-up coordinates (not original survey metres).")
    args = parser.parse_args()
    if args.chunk <= 0:
        parser.error("Chunk size must be positive.")
    if not args.config.is_file():
        parser.error("Training config.yml does not exist.")
    bake(args)


if __name__ == "__main__":
    main()
