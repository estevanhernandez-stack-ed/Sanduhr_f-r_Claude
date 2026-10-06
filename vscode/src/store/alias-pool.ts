/**
 * Neutral display names for projects: birds, minerals, rivers and stars. Chosen to read
 * as nature words, not as product or company names. Pool size is part of the contract
 * only as "about 100".
 */
const BIRDS = [
  'Kestrel', 'Heron', 'Plover', 'Wren', 'Curlew', 'Egret', 'Osprey', 'Tern', 'Linnet', 'Pipit',
  'Bittern', 'Dunlin', 'Godwit', 'Lapwing', 'Shrike', 'Siskin', 'Teal', 'Whimbrel', 'Redstart',
  'Nuthatch', 'Harrier', 'Avocet', 'Fulmar', 'Skua', 'Dipper',
];
const MINERALS = [
  'Basalt', 'Quartz', 'Feldspar', 'Gypsum', 'Dolomite', 'Garnet', 'Obsidian', 'Calcite', 'Beryl',
  'Olivine', 'Jasper', 'Agate', 'Pumice', 'Schist', 'Gneiss', 'Granite', 'Talc', 'Mica',
  'Fluorite', 'Apatite', 'Zircon', 'Rutile', 'Epidote', 'Barite', 'Hematite',
];
const RIVERS = [
  'Tamar', 'Severn', 'Wye', 'Tweed', 'Exe', 'Tyne', 'Trent', 'Ouse', 'Orwell', 'Nene', 'Swale',
  'Eden', 'Lune', 'Kennet', 'Itchen', 'Stour', 'Medway', 'Teme', 'Usk', 'Taff', 'Teifi', 'Mersey',
  'Fal', 'Yare', 'Axe',
];
const STARS = [
  'Rigel', 'Deneb', 'Mizar', 'Alcor', 'Spica', 'Antares', 'Capella', 'Castor', 'Pollux', 'Algol',
  'Mira', 'Sadr', 'Naos', 'Tarazed', 'Alphard', 'Mintaka', 'Alnitak', 'Saiph', 'Kochab', 'Merak',
  'Dubhe', 'Thuban', 'Vindemiatrix', 'Zosma', 'Menkar',
];

export const ALIAS_POOL: readonly string[] = [...BIRDS, ...MINERALS, ...RIVERS, ...STARS].map(
  (w) => `Project ${w}`,
);

/**
 * Pick an unused alias. Deterministic: the scan starts at `seed` (default: how many names
 * are already used) and walks the pool, so the same store always yields the same next name.
 * When every pool name is taken, a number is appended: "Project Kestrel 2", "... 3", and so on.
 */
export function nextAlias(used: ReadonlySet<string>, seed: number = used.size): string {
  const n = ALIAS_POOL.length;
  for (let round = 1; ; round++) {
    for (let i = 0; i < n; i++) {
      const base = ALIAS_POOL[(((seed + i) % n) + n) % n];
      const name = round === 1 ? base : `${base} ${round}`;
      if (!used.has(name)) return name;
    }
  }
}
