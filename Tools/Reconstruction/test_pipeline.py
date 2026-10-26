"""Focused format and camera validation; no CUDA or network required."""
import importlib.util
import json
from pathlib import Path
import tempfile
import unittest
import numpy as np
from PIL import Image

spec = importlib.util.spec_from_file_location("prepare_ply", Path(__file__).with_name("prepare_ply.py"))
ply = importlib.util.module_from_spec(spec)
spec.loader.exec_module(ply)


class GaussianConversionTests(unittest.TestCase):
    def source(self, path, rest=9):
        names = ["x", "y", "z"] + [f"f_dc_{i}" for i in range(3)] + [f"f_rest_{i}" for i in range(rest)] + \
                ["opacity"] + [f"scale_{i}" for i in range(3)] + [f"rot_{i}" for i in range(4)]
        values = [1, 2, 3, .1, .2, .3] + list(range(rest)) + [0, -2, -3, -4, 2, 0, 0, 0]
        path.write_text("ply\nformat ascii 1.0\nelement vertex 1\n" +
                        "".join(f"property float {name}\n" for name in names) + "end_header\n" +
                        " ".join(map(str, values)) + "\n", encoding="ascii")

    def test_degree_one_padding_and_normalization(self):
        with tempfile.TemporaryDirectory() as folder:
            source, target = Path(folder)/"in.ply", Path(folder)/"out.ply"
            self.source(source)
            self.assertEqual(ply.normalize(source, target), 1)
            data = ply.read_ply(target)
            self.assertEqual(list(data), ply.NAMES)
            self.assertEqual(len(data), 62)
            self.assertEqual(data["f_rest_15"][0], 3)
            self.assertEqual(data["f_rest_30"][0], 6)
            self.assertEqual(data["f_rest_3"][0], 0)
            self.assertEqual(data["rot_0"][0], 1)
            self.assertEqual(data["nx"][0], 0)
            with self.assertRaises(ValueError):
                ply.normalize(source, target)

    def test_point_cloud_rejected(self):
        with tempfile.TemporaryDirectory() as folder:
            source, target = Path(folder)/"in.ply", Path(folder)/"out.ply"
            source.write_text("ply\nformat ascii 1.0\nelement vertex 1\nproperty float x\nend_header\n0\n")
            with self.assertRaises(ValueError):
                ply.normalize(source, target)

    def test_invalid_sh_rejected(self):
        with tempfile.TemporaryDirectory() as folder:
            source, target = Path(folder)/"in.ply", Path(folder)/"out.ply"
            self.source(source, 12)
            with self.assertRaises(ValueError):
                ply.normalize(source, target)

    def test_empty_after_filter_rejected(self):
        with tempfile.TemporaryDirectory() as folder:
            source, target = Path(folder)/"in.ply", Path(folder)/"out.ply"
            self.source(source)
            with self.assertRaises(ValueError):
                ply.normalize(source, target, min_opacity=.9)


class SyntheticCaptureTests(unittest.TestCase):
    def test_camera_poses_images_and_split(self):
        root = Path(__file__).resolve().parents[2]/"TrainingData/synthetic_sand_torus"
        if not (root/"transforms.json").exists():
            self.skipTest("Generate synthetic dataset in Unity first")
        metadata = json.loads((root/"transforms.json").read_text(encoding="utf-8-sig"))
        self.assertEqual(len(metadata["frames"]), 48)
        train, val = set(metadata["train_filenames"]), set(metadata["val_filenames"])
        self.assertEqual((len(train), len(val)), (42, 6))
        self.assertFalse(train & val)
        self.assertEqual(train | val, {f["file_path"] for f in metadata["frames"]})
        self.assertTrue((root/metadata["ply_file_path"]).is_file())
        target = np.array([0, 0, .55])
        for frame in metadata["frames"]:
            pose = np.array(frame["transform_matrix"])
            r = pose[:3, :3]
            np.testing.assert_allclose(r.T @ r, np.eye(3), atol=1e-5)
            self.assertAlmostEqual(np.linalg.det(r), 1, places=5)
            direction = target-pose[:3, 3]
            direction /= np.linalg.norm(direction)
            np.testing.assert_allclose(direction, -pose[:3, 2], atol=1e-5)
            image = Image.open(root/frame["file_path"])
            self.assertEqual(image.size, (metadata["w"], metadata["h"]))
            self.assertGreater(float(np.asarray(image).std()), 10)


if __name__ == "__main__":
    unittest.main(verbosity=2)
