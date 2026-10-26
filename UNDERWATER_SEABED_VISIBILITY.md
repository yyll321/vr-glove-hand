# Seabed 能见度调整（2026-10-26）

- 自然海底区的投射焦散强度设为 0；水池仍保留波光。海面几何和反射保留。
- 同档位 seabed 悬浮粒子预算乘以 1.7；高档 18360、中档 14688、低档约 10098，不包含独立泥沙粒子。保持原 2–8 mm 粒径，继续沿公共世界流场运动。切换水域时自动切换预算。
- 增加仅作用于 seabed 的高度浑浊层：额外消光系数为 `0.38 * (1-saturate(y/8))²` 每米。`y` 是距原场景海底零高度的米数。沿视线对这层浓度解析积分，使低处最浑浊、上升逐渐清晰；朝海底看时仍会穿过下层浑水。
- 远处背景增加最多 3.5 像素的深度感知柔化，与同一浑浊光路联动，1.2 米以内不主动柔化；HUD 在此阶段之后绘制。XR 使用各眼自己的深度、色彩切片和位置，无额外反射相机。
- 浑浊消光用于不透明背景、透明粒子、水下反射路径和工作灯衰减，避免粒子或灯在浑水中仍异常清晰。

保存位置：`Assets/Underwater/Scenes/UnderwaterResearchEditable.unity`。修改前场景备份：`Assets/Underwater/Backups/UnderwaterResearch-beforeSeabedHaze-20261026-201925.unity`。

可调参数：`Assets/Underwater/Settings/UnderwaterRenderer.asset` 的 `Horizon Line Fog and Color > Seabed visibility` 中，`Sea Bottom Haze` 控制底层浓度，`Sea Haze Top` 控制恢复清晰的高度，`Sea Blur Pixels` 控制柔化半径，`Sea Caustic Strength` 当前为 0。粒子倍率在场景 `UnderwaterEnvironment.seabedParticleMultiplier` 中。

验证：129 项 Windows 自动检查通过；同为 6 米观察距离的白色背景，低处红通道 0.133，上层 0.475；seabed 开关全局焦散前后实际图像差为 0，水池焦散对比仍通过。截图 `Logs/UnderwaterResearch/02-seabed.png`、`11-seabed-low-visibility.png`、`12-seabed-upper-visibility.png`。

DX11 PICO Live Preview seabed 专项运行 10 秒，通过：头显激活、双眼纹理 1920×1920、运行错误列表为空。报告 `Logs/UnderwaterSeabed/live-preview-check.json`。该检查不代替佩戴者对浑浊强度的主观确认。
