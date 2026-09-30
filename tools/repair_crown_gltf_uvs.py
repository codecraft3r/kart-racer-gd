"""Add planar UVs to Crown Victoria GLB primitives that have none.

Godot generates tangents while importing these assets, including collision and
glass meshes. glTF permits missing UVs, but Godot reports an import error for
each affected primitive. This preserves the existing meshes and scene graph.
"""

from __future__ import annotations

import json
from pathlib import Path
import struct


ASSET_DIR = Path(__file__).resolve().parents[1] / "assets" / "crown-victoria"
FLOAT = 5126
JSON_CHUNK = 0x4E4F534A
BIN_CHUNK = 0x004E4942


def pad(data: bytes, fill: bytes) -> bytes:
    return data + fill * ((-len(data)) % 4)


def repair(path: Path) -> int:
    data = path.read_bytes()
    magic, version, total = struct.unpack_from("<4sII", data)
    if (magic, version, total) != (b"glTF", 2, len(data)):
        raise ValueError(f"Unexpected GLB header: {path}")

    offset = 12
    chunks: dict[int, bytes] = {}
    while offset < len(data):
        length, kind = struct.unpack_from("<II", data, offset)
        offset += 8
        chunks[kind] = data[offset : offset + length]
        offset += length
    if set(chunks) != {JSON_CHUNK, BIN_CHUNK}:
        raise ValueError(f"Unexpected GLB chunks: {path}")

    gltf = json.loads(chunks[JSON_CHUNK])
    binary = bytearray(chunks[BIN_CHUNK])
    repaired = 0
    for mesh in gltf.get("meshes", []):
        for primitive in mesh["primitives"]:
            attributes = primitive["attributes"]
            if "TEXCOORD_0" in attributes:
                continue

            position = gltf["accessors"][attributes["POSITION"]]
            if position["componentType"] != FLOAT or position["type"] != "VEC3":
                raise ValueError(f"Unexpected position format: {path}")
            view = gltf["bufferViews"][position["bufferView"]]
            stride = view.get("byteStride", 12)
            start = view.get("byteOffset", 0) + position.get("byteOffset", 0)
            vertices = [
                struct.unpack_from("<3f", binary, start + i * stride)
                for i in range(position["count"])
            ]
            low = [min(vertex[axis] for vertex in vertices) for axis in range(3)]
            high = [max(vertex[axis] for vertex in vertices) for axis in range(3)]
            axes = sorted(range(3), key=lambda axis: high[axis] - low[axis], reverse=True)[:2]

            uv_offset = len(binary)
            for vertex in vertices:
                uv = [
                    (vertex[axis] - low[axis]) / max(high[axis] - low[axis], 1e-6)
                    for axis in axes
                ]
                binary.extend(struct.pack("<2f", *uv))
            gltf["bufferViews"].append(
                {"buffer": 0, "byteOffset": uv_offset, "byteLength": len(vertices) * 8}
            )
            gltf["accessors"].append(
                {
                    "bufferView": len(gltf["bufferViews"]) - 1,
                    "componentType": FLOAT,
                    "count": len(vertices),
                    "type": "VEC2",
                }
            )
            attributes["TEXCOORD_0"] = len(gltf["accessors"]) - 1
            repaired += 1

    if repaired:
        binary = bytearray(pad(bytes(binary), b"\0"))
        gltf["buffers"][0]["byteLength"] = len(binary)
        json_data = pad(json.dumps(gltf, separators=(",", ":")).encode(), b" ")
        output = bytearray(struct.pack("<4sII", b"glTF", 2, 12 + 8 + len(json_data) + 8 + len(binary)))
        output.extend(struct.pack("<II", len(json_data), JSON_CHUNK))
        output.extend(json_data)
        output.extend(struct.pack("<II", len(binary), BIN_CHUNK))
        output.extend(binary)
        path.write_bytes(output)
    return repaired


if __name__ == "__main__":
    for asset in sorted(ASSET_DIR.glob("*.glb")):
        print(f"{asset.name}: added UVs to {repair(asset)} primitives")
