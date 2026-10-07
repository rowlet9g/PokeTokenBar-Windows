"""Build pinned, offline Gen 1-9 metadata and front sprites from PokeAPI.

Run from the repository root. Downloads are cached under Windows/artifacts.
Requires Pillow to decode and validate each normal/shiny PNG before packaging.
"""
import concurrent.futures
import hashlib
import io
import json
import pathlib
import time
import urllib.request
import zipfile
from datetime import datetime, timezone
from PIL import Image

ROOT = pathlib.Path(__file__).resolve().parents[2]
CACHE = ROOT / "Windows/artifacts/catalog-source"
OUTPUT = ROOT / "Windows/src/PokeTokenBar.Core/Data"
MAX_ID = 1025
CACHE.mkdir(parents=True, exist_ok=True)
def fetch_json(url, data=None):
    request = urllib.request.Request(url, data=data, headers={"User-Agent": "PokeTokenBar-catalog-build", "Content-Type": "application/json"})
    with urllib.request.urlopen(request, timeout=60) as response:
        return json.load(response)

if not (CACHE / "species.json").exists():
    query = "{ pokemonspecies(where: {id: {_gte: 1, _lte: 1025}}, order_by: {id: asc}) { id name capture_rate is_legendary is_mythical evolves_from_species_id generation_id pokemonspeciesnames(where: {language_id: {_in: [3,9]}}) { name language_id } } }"
    (CACHE / "species.json").write_text(json.dumps(fetch_json("https://graphql.pokeapi.co/v1beta2", json.dumps({"query":query}).encode())), encoding="utf-8")
if not (CACHE / "sprite-commit.txt").exists():
    (CACHE / "sprite-commit.txt").write_text(fetch_json("https://api.github.com/repos/PokeAPI/sprites/commits/master")["sha"], encoding="utf-8")
COMMIT = (CACHE / "sprite-commit.txt").read_text(encoding="utf-8-sig").strip()
rows = json.loads((CACHE / "species.json").read_text(encoding="utf-8-sig"))["data"]["pokemonspecies"]
assert [r["id"] for r in rows] == list(range(1, MAX_ID + 1)), "Incomplete species catalog"
OUTPUT.mkdir(parents=True, exist_ok=True)
license_url = f"https://raw.githubusercontent.com/PokeAPI/sprites/{COMMIT}/LICENCE.txt"
with urllib.request.urlopen(license_url, timeout=30) as response:
    (OUTPUT / "SPRITE_LICENSE.txt").write_bytes(response.read())
(OUTPUT / "SPRITE_SOURCE.txt").write_text(
    f"Pokémon species metadata: https://graphql.pokeapi.co/v1beta2\n"
    f"Sprite source: https://github.com/PokeAPI/sprites/tree/{COMMIT}/sprites/pokemon\n"
    f"Bundled scope: National Pokédex #1-{MAX_ID}, normal and shiny front sprites.\n"
    "Generation 6+ default sprites include community-maintained Gen 5-style artwork.\n"
    f"Contributors: https://github.com/PokeAPI/sprites/blob/{COMMIT}/CONTRIBUTING_SPRITES.md\n"
    "Original repository license notice is included in PokeAPI-sprites-LICENSE.txt.\n"
    "Pokémon image contents are Copyright The Pokémon Company.\n", encoding="utf-8")
sprite_cache = CACHE / "sprites"
sprite_cache.mkdir(exist_ok=True)

def download(spec):
    species_id, shiny = spec
    name = f"shiny-{species_id}.png" if shiny else f"{species_id}.png"
    path = sprite_cache / name
    if path.exists():
        data = path.read_bytes()
    else:
        url = f"https://raw.githubusercontent.com/PokeAPI/sprites/{COMMIT}/sprites/pokemon/{'shiny/' if shiny else ''}{species_id}.png"
        for attempt in range(4):
            try:
                request = urllib.request.Request(url, headers={"User-Agent": "PokeTokenBar-catalog-build"})
                with urllib.request.urlopen(request, timeout=30) as response:
                    data = response.read(1_048_577)
                break
            except Exception:
                if attempt == 3:
                    raise
                time.sleep(1 + attempt)
        path.write_bytes(data)
    assert data.startswith(b"\x89PNG\r\n\x1a\n") and len(data) <= 1_048_576, name
    with Image.open(io.BytesIO(data)) as image:
        image.load()
        assert image.format == "PNG" and image.size == (96, 96), (name, image.size)
        rgba = image.convert("RGBA")
        assert rgba.getchannel("A").getextrema()[1] > 0, f"Empty sprite: {name}"
    return name, data

species = []
for row in rows:
    names = {n["language_id"]: n["name"] for n in row["pokemonspeciesnames"]}
    assert names.get(3) and names.get(9), f"Missing localized name: {row['id']}"
    assert 0 <= row["capture_rate"] <= 255 and 1 <= row["generation_id"] <= 9
    parent = row["evolves_from_species_id"]
    assert parent is None or 1 <= parent <= MAX_ID
    species.append(dict(id=row["id"], name=row["name"], koreanName=names[3], englishName=names[9],
        captureRate=row["capture_rate"], isLegendary=row["is_legendary"], isMythical=row["is_mythical"],
        evolvesFromSpeciesId=parent, generation=row["generation_id"]))

assets = {}
with concurrent.futures.ThreadPoolExecutor(max_workers=8) as pool:
    futures = {pool.submit(download, (i, shiny)) for i in range(1, MAX_ID + 1) for shiny in (False, True)}
    for future in concurrent.futures.as_completed(futures):
        name, data = future.result()
        assets[name] = data
        if len(assets) % 100 == 0:
            print(f"Validated {len(assets)}/2050 sprites", flush=True)

with zipfile.ZipFile(OUTPUT / "pokemon-sprites.zip", "w", zipfile.ZIP_DEFLATED, compresslevel=9) as archive:
    for name, data in sorted(assets.items()):
        entry = zipfile.ZipInfo(name, date_time=(2026, 1, 1, 0, 0, 0))
        entry.compress_type = zipfile.ZIP_DEFLATED
        archive.writestr(entry, data)

catalog = dict(schemaVersion=1, maximumSpeciesId=MAX_ID, source="https://graphql.pokeapi.co/v1beta2",
    retrievedAt=datetime.now(timezone.utc).isoformat(), spriteSource=f"https://github.com/PokeAPI/sprites/tree/{COMMIT}",
    species=species, spriteHashes={name: hashlib.sha256(data).hexdigest() for name, data in sorted(assets.items())})
(OUTPUT / "pokemon-catalog.json").write_text(json.dumps(catalog, ensure_ascii=False, separators=(",", ":")) + "\n", encoding="utf-8")
print(f"Catalog complete: {len(species)} species, {len(assets)} decoded sprites; ZIP {(OUTPUT / 'pokemon-sprites.zip').stat().st_size} bytes", flush=True)
