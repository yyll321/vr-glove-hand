# Marine grasp target assets

Downloaded on 2026-10-27 from the public model pages below. These are third-party 3D assets converted to Unity OBJ and URP materials, not generated substitute geometry.

## Starfish
- Title: Starfish
- Creator: Poly by Google
- Source: https://poly.pizza/m/6H-0K9IEr56
- License: Creative Commons Attribution 3.0 Unported (CC BY 3.0)
- License text: https://creativecommons.org/licenses/by/3.0/legalcode
- Original model URL: https://static.poly.pizza/c9c1bc97-d76e-4e87-bd3a-87ab44b78aac.glb
- Changes: selected the orange variant from the three provided variants, centered, scaled to 0.22 m, converted to OBJ; original texture retained. Added a box collision proxy and grasp behavior.
- 2026-10-28 grasp revision: `StarfishThickZ.asset` derives from that OBJ. Converted its original Y thickness to local Z and increased thickness to 0.075 m, with a -90 degree X visual rotation to lay it flat in Unity. Original footprint, UVs and texture retained; normals/tangents recalculated. This is an intentionally thickened grasp-test proxy, not a biological dimensional model.

## Squid
- Title: Squid
- Creator: Quaternius
- Source: https://poly.pizza/m/gReSPnGdkr
- License: CC0 1.0 Universal / public-domain dedication
- License text: https://creativecommons.org/publicdomain/zero/1.0/legalcode
- Original model URL: https://static.poly.pizza/217fe0f2-99e6-4b3f-914a-30833f40b98b.glb
- Changes: rotated to rest horizontally, centered, scaled to 0.28 m, converted to OBJ; original atlas retained. Added a box collision proxy and grasp behavior. This is a stylized static model, without live-animal or soft-body simulation.

Conversion script: `Tools/import_marine_models.py`. Preserve this attribution file when sharing the assets or presentation images.
