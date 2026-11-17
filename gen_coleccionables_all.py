import json, os

raw_gs = json.load(open(r"E:\OcarinaRemake\skulltulas_100.json", encoding="utf-8"))
maestro = json.load(open(r"E:\OcarinaRemake\catalogo_maestro.json", encoding="utf-8"))

ZONES = {
    "InsideTheDekuTree": "Árbol Deku", "DodongoCavern": "Caverna de los Dodongos",
    "InsideJabuJabuBelly": "Barriga de Jabu-Jabu", "ForestTemple": "Templo del Bosque",
    "FireTemple": "Templo del Fuego", "WaterTemple": "Templo del Agua",
    "ShadowTemple": "Templo de las Sombras", "SpiritTemple": "Templo del Espíritu",
    "BottomOfTheWell": "Fondo del Pozo", "IceCavern": "Caverna de Hielo",
    "GanonCastle": "Castillo de Ganon", "KokiriForest": "Bosque Kokiri",
    "LostWood": "Bosque Perdido", "SacredForestMeadow": "Pradera Sagrada del Bosque",
    "HyruleField": "Pradera de Hyrule", "Market": "Mercado de Hyrule",
    "HyruleCastle": "Castillo de Hyrule", "LonLonRanch": "Rancho Lon Lon",
    "KakarikoVillage": "Aldea Kakariko", "Graveyard": "Cementerio de Kakariko",
    "DeathMountainTrail": "Sendero de la Montaña de la Muerte",
    "DeathMountainCrater": "Cráter de la Montaña de la Muerte",
    "GoronCity": "Ciudad Goron", "ZoraRiver": "Río Zora",
    "ZoraDomain": "Dominio de los Zora", "ZoraFountain": "Fuente de los Zora",
    "LakeHylia": "Lago Hylia", "GerudoValley": "Valle Gerudo",
    "GerudoFortress": "Fortaleza Gerudo", "HountedWasteland": "Yermo Encantado",
    "DesertColossus": "Coloso del Desierto",
}

def floor_tag(b, y):
    if b == "InsideTheDekuTree": return "3F" if y >= 127 else "2F" if y >= 117 else "1F" if y >= 106 else "B1" if y >= 93 else "B2"
    if b == "DodongoCavern": return "2F" if y >= 108 else "1F"
    if b == "InsideJabuJabuBelly": return "2F" if y >= 108 else "1F" if y >= 98 else "B1"
    if b == "BottomOfTheWell": return "B1" if y >= 98 else "B2"
    if b == "ForestTemple": return "3F" if y >= 122 else "2F" if y >= 111 else "1F" if y >= 100 else "B1" if y >= 90 else "B2"
    if b == "FireTemple": return "5F" if y >= 142 else "4F" if y >= 131 else "3F" if y >= 120 else "2F" if y >= 109 else "1F"
    if b == "WaterTemple": return "3F" if y >= 122 else "2F" if y >= 111 else "1F" if y >= 100 else "B1"
    if b == "ShadowTemple": return "B1" if y >= 95 else "B2" if y >= 82 else "B3" if y >= 68 else "B4"
    if b == "SpiritTemple": return "4F" if y >= 134 else "3F" if y >= 122 else "2F" if y >= 110 else "1F"
    return ""

# 1. Las 100 Skulltulas Doradas
by_hash = {}
for item in raw_gs:
    h = int(item["Hash64"])
    banc = item["Banc"].replace("_Static", "").replace("_Dynamic", "")
    e = by_hash.setdefault(h, {"bancs": set(), "X": item["X"], "Y": item["Y"], "Z": item["Z"]})
    e["bancs"].add(banc)

def _sk_gs(kv):
    h, d = kv
    return (sorted(d["bancs"]), -d["Y"], d["X"])

items_out, gs_counts = [], {}
for h, info in sorted(by_hash.items(), key=_sk_gs):
    bancs = sorted(info["bancs"])
    base = bancs[0].replace("_Child", "").replace("_Adult", "")
    if base not in ZONES: continue
    hc, ha = any("_Child" in b for b in bancs), any("_Adult" in b for b in bancs)
    epoca = "Niño / Adulto" if (hc and ha) or (not hc and not ha) else ("Niño" if hc else "Adulto")
    zona = ZONES[base]
    gs_counts[zona] = gs_counts.get(zona, 0) + 1
    piso = floor_tag(base, info["Y"])
    piso_txt = f" · Piso {piso}" if piso else ""
    items_out.append({
        "Id": f"gs_{h:016x}", "Zona": zona, "Mapa": base, "Tipo": "Skulltula Dorada",
        "Nombre": f"Skulltula #{gs_counts[zona]:02d} ({zona})",
        "UbicacionExacta": f"{zona}{piso_txt} (X: {info['X']}, Y: {info['Y']}, Z: {info['Z']})",
        "Epoca": epoca, "WorldX": info["X"], "WorldY": info["Y"], "WorldZ": info["Z"],
        "Hash": f"{h:016x}", "DireccionBit": "", "Bit": 0
    })

# 2. Las 36 Piezas de Corazón
hp_list = [x for x in maestro if x.get("Tipo") == "Pieza de Corazón"]
hp_list.sort(key=lambda x: (x["Mapa"], -x["WorldY"], x["WorldX"]))
hp_counts = {}
for idx, x in enumerate(hp_list, 1):
    base = x["Mapa"]
    if base not in ZONES: continue
    h = int(x["ActorHash"])
    zona = ZONES[base]
    hp_counts[zona] = hp_counts.get(zona, 0) + 1
    items_out.append({
        "Id": f"hp_{h:016x}", "Zona": zona, "Mapa": base, "Tipo": "Pieza de Corazón",
        "Nombre": f"Pieza de Corazón #{hp_counts[zona]:02d} ({zona})",
        "UbicacionExacta": f"{zona} (X: {x['WorldX']}, Y: {x['WorldY']}, Z: {x['WorldZ']})",
        "Epoca": x["Epoca"], "WorldX": x["WorldX"], "WorldY": x["WorldY"], "WorldZ": x["WorldZ"],
        "Hash": f"{h:016x}", "DireccionBit": "", "Bit": 0
    })

# 3. Los 73 Frijoles Mágicos
fm_list = [x for x in maestro if x.get("Tipo") == "Frijol Mágico"]
fm_list.sort(key=lambda x: (x["Mapa"], -x["WorldY"], x["WorldX"]))
fm_counts = {}
for idx, x in enumerate(fm_list, 1):
    base = x["Mapa"]
    if base not in ZONES: continue
    h = int(x["ActorHash"])
    zona = ZONES[base]
    fm_counts[zona] = fm_counts.get(zona, 0) + 1
    piso = floor_tag(base, x["WorldY"])
    piso_txt = f" · Piso {piso}" if piso else ""
    items_out.append({
        "Id": f"fm_{h:016x}", "Zona": zona, "Mapa": base, "Tipo": "Frijol Mágico",
        "Nombre": f"Frijol Mágico #{fm_counts[zona]:02d} ({zona})",
        "UbicacionExacta": f"{zona}{piso_txt} (X: {x['WorldX']}, Y: {x['WorldY']}, Z: {x['WorldZ']})",
        "Epoca": x["Epoca"], "WorldX": x["WorldX"], "WorldY": x["WorldY"], "WorldZ": x["WorldZ"],
        "Hash": f"{h:016x}", "DireccionBit": "", "Bit": 0
    })

# 4. Cofres del Tesoro (desde coleccionables_extraidos.json)
extraidos = json.load(open(r"E:\OcarinaRemake\coleccionables_extraidos.json", encoding="utf-8"))
CHEST_GYAMLS = {
    "Obj_TBox_A_01", "Obj_TBox_A_02", "Obj_TBox_A_03", "Obj_TBox_A_04", "Obj_TBox_A_06",
    "Obj_TBox_A_01_NoBake", "Obj_TBox_A_02_NoBake", "Obj_TBox_A_03_NoBake", "Obj_TBox_A_06_NoBake",
    "Obj_TBox_GlassTruth_A_01", "Obj_TBox_A_01_memo11", "TBox_Rupee_Red"
}
chest_by_hash = {}
for x in extraidos:
    if x.get("Gyaml") not in CHEST_GYAMLS: continue
    h = int(x["Hash"])
    if h not in chest_by_hash:
        chest_by_hash[h] = x

chest_list = sorted(chest_by_hash.values(), key=lambda x: (x["Mapa"], -x["WorldY"], x["WorldX"]))
chest_counts = {}
for x in chest_list:
    base = x["Mapa"]
    if base not in ZONES: continue
    h = int(x["Hash"])
    zona = ZONES[base]
    chest_counts[zona] = chest_counts.get(zona, 0) + 1
    piso = floor_tag(base, x["WorldY"])
    piso_txt = f" · Piso {piso}" if piso else ""
    ep_raw = x.get("Epoca", "Ambos")
    epoca = "Niño / Adulto" if ep_raw in ("Ambos", "") else ep_raw
    items_out.append({
        "Id": f"tb_{h:016x}", "Zona": zona, "Mapa": base, "Tipo": "Cofre",
        "Nombre": f"Cofre #{chest_counts[zona]:02d} ({zona})",
        "UbicacionExacta": f"{zona}{piso_txt} (X: {x['WorldX']}, Y: {x['WorldY']}, Z: {x['WorldZ']})",
        "Epoca": epoca, "WorldX": x["WorldX"], "WorldY": x["WorldY"], "WorldZ": x["WorldZ"],
        "Hash": f"{h:016x}", "DireccionBit": "", "Bit": 0
    })

for out_path in [r"dist\coleccionables.json", os.path.join(os.environ["APPDATA"], "PhykroTrainer", "coleccionables.json")]:
    os.makedirs(os.path.dirname(out_path), exist_ok=True)
    with open(out_path, "w", encoding="utf-8") as f:
        json.dump(items_out, f, indent=2, ensure_ascii=False)
print(f"Total escritos: {len(items_out)} (100 Skulltulas + {len(hp_list)} Piezas de Corazón + {len(fm_list)} Frijoles Mágicos + {len(chest_list)} Cofres).")


