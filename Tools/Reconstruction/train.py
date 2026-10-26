"""Thin, reproducible Nerfstudio CLI workflow. Training runs outside Unity."""
import argparse
from pathlib import Path
import shlex
import shutil
import subprocess
import sys
import importlib.metadata
import importlib.util
import json


def command(args):
    if args.action == "prepare":
        return ["ns-process-data", args.input_type, "--data", str(args.input.resolve()),
                "--output-dir", str(args.data.resolve())] + (
                    ["--num-frames-target", str(args.frames)] if args.input_type == "video" else [])
    if args.action == "train":
        cmd = ["ns-train", args.method,
                "--output-dir", str(args.output.resolve()), "--max-num-iterations", str(args.steps),
                "--vis", "tensorboard"]
        if args.method == "splatfacto":
            cmd += ["--pipeline.model.sh-degree", str(args.sh_degree),
                    "--pipeline.model.stop-split-at", str(min(args.stop_split_at, args.steps))]
        cmd += [args.parser, "--data", str(args.data.resolve()), "--downscale-factor", str(args.downscale_factor)]
        if args.parser == "colmap":
            cmd += ["--images-path", args.images_path]
        return cmd
    return ["ns-export", "gaussian-splat", "--load-config", str(args.config.resolve()),
            "--output-dir", str(args.output.resolve())]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    sub = parser.add_subparsers(dest="action", required=True)
    sub.add_parser("preflight", help="Read-only training environment check; installs nothing")
    prepare = sub.add_parser("prepare")
    prepare.add_argument("--input", type=Path, required=True)
    prepare.add_argument("--input-type", choices=["video", "images"], default="video")
    prepare.add_argument("--frames", type=int, default=250)
    prepare.add_argument("--data", type=Path, required=True)
    train = sub.add_parser("train")
    train.add_argument("--method", choices=["splatfacto", "nerfacto"], default="splatfacto")
    train.add_argument("--data", type=Path, required=True)
    train.add_argument("--output", type=Path, required=True)
    train.add_argument("--steps", type=int, default=7000)
    train.add_argument("--parser", choices=["nerfstudio", "colmap"], default="nerfstudio")
    train.add_argument("--images-path", default="images_wb")
    train.add_argument("--downscale-factor", type=int, choices=[1, 2, 4, 8], default=2)
    train.add_argument("--sh-degree", type=int, choices=[0, 1, 2, 3], default=1)
    train.add_argument("--stop-split-at", type=int, default=5000)
    export = sub.add_parser("export-gs")
    export.add_argument("--config", type=Path, required=True)
    export.add_argument("--output", type=Path, required=True)
    for action in (prepare, train, export):
        action.add_argument("--dry-run", action="store_true")
    args = parser.parse_args()
    if args.action == "preflight":
        report = {"python": sys.executable,
                  "commands": {name: shutil.which(name) for name in ["ns-train", "ns-export", "nvcc", "colmap", "ffmpeg"]},
                  "packages": {}}
        for name in ["torch", "nerfstudio", "gsplat"]:
            try:
                report["packages"][name] = importlib.metadata.version(name)
            except importlib.metadata.PackageNotFoundError:
                report["packages"][name] = None
        if importlib.util.find_spec("torch"):
            import torch
            report["cuda_available"] = torch.cuda.is_available()
            report["torch_cuda"] = torch.version.cuda
            if report["cuda_available"]:
                report["gpu"] = torch.cuda.get_device_name(0)
                report["vram_gib"] = round(torch.cuda.get_device_properties(0).total_memory / 1024**3, 2)
        report["ready_for_training"] = bool(report.get("cuda_available") and report["commands"]["ns-train"] and report["commands"]["nvcc"])
        print(json.dumps(report, indent=2, ensure_ascii=False))
        return
    cmd = command(args)
    print(shlex.join(cmd), flush=True)
    if args.dry_run:
        return
    if not shutil.which(cmd[0]):
        parser.error(f"{cmd[0]} is missing. Activate the Nerfstudio environment; see RECONSTRUCTION_TRAINING.md.")
    if args.action == "prepare":
        if not args.input.exists():
            parser.error("Input video/image directory does not exist.")
        if args.frames < 2:
            parser.error("Use at least two target frames.")
        if args.data.exists() and any(args.data.iterdir()):
            parser.error("Dataset output must be empty to avoid overwriting camera calibration.")
    elif args.action == "train":
        if args.parser == "nerfstudio" and not (args.data / "transforms.json").is_file():
            parser.error("Missing transforms.json; run prepare and inspect COLMAP registration first.")
        if args.parser == "colmap" and not ((args.data / "sparse").exists() or (args.data / "colmap/sparse").exists()):
            parser.error("Missing COLMAP sparse directory.")
        if args.parser == "colmap" and not (args.data / args.images_path).is_dir():
            parser.error("COLMAP image directory does not exist.")
        if args.steps <= 0:
            parser.error("Training steps must be positive.")
        if args.stop_split_at <= 0:
            parser.error("Stop-split-at must be positive.")
        import torch
        if not torch.cuda.is_available():
            parser.error("CUDA GPU unavailable. Verify the PyTorch/CUDA environment.")
        print("GPU:", torch.cuda.get_device_name(0), flush=True)
    elif not args.config.is_file():
        parser.error("Training config.yml does not exist.")
    subprocess.run(cmd, check=True)


if __name__ == "__main__":
    try:
        main()
    except subprocess.CalledProcessError as exc:
        sys.exit(exc.returncode)
