import {
  BatteryCharging,
  Cog,
  Disc3,
  Droplets,
  Flame,
  Fuel,
  HardHat,
  Lightbulb,
  Package,
  PaintRoller,
  Car,
  Ruler,
  ShieldCheck,
  Sprout,
  Wind,
  Wrench,
  Zap,
  type LucideIcon,
} from "lucide-react";

/**
 * Icon fallback for a category card (the API has no category image).
 * Matching is by name keywords (Tajik / Russian / English); the order matters — specific before generic.
 * Anything unknown gets a neutral package icon. Nothing here is data: it only picks a picture.
 */
const ICON_RULES: Array<[RegExp, LucideIcon]> = [
  [/аккумул|battery|cordless/, BatteryCharging],
  [/бензин|petrol|gasoline/, Fuel],
  [/кафшер|свар|weld|solder/, Flame],
  [/компрессор|пневмат|compress|pneumat/, Wind],
  [/ченку|измер|measur/, Ruler],
  [/сохтмон|строит|construct/, HardHat],
  [/боғдор|сад[оа]в|садов|garden/, Sprout],
  [/насос|водо|тачҳизоти об|таҷҳизоти об|pump|water/, Droplets],
  [/рӯшно|рушно|освещ|light|lamp/, Lightbulb],
  [/лавозимот|запчаст|эҳтиёт|spare|parts|accessor/, Cog],
  [/диск|бур|сверл|disc|drill bit|bits/, Disc3],
  [/муҳофиз|мухофиз|защит|protect|safety/, ShieldCheck],
  [/ранг|краск|ороиш|paint|decor/, PaintRoller],
  [/гараж|автомоб|garage|auto|car\b/, Car],
  [/дастӣ|дасти|ручн|hand/, Wrench],
  [/барқ|электр|electric|power/, Zap],
];

export function categoryIcon(name: string): LucideIcon {
  const n = name.toLowerCase();
  for (const [re, icon] of ICON_RULES) if (re.test(n)) return icon;
  return Package;
}

export interface CategoryTone {
  card: string;
  tile: string;
  arrow: string;
}

// Full class strings (Tailwind must see them literally).
export const TONES: CategoryTone[] = [
  { card: "from-blue-50 to-white border-blue-100", tile: "bg-blue-100 text-blue-600", arrow: "text-blue-600" },
  { card: "from-emerald-50 to-white border-emerald-100", tile: "bg-emerald-100 text-emerald-600", arrow: "text-emerald-600" },
  { card: "from-orange-50 to-white border-orange-100", tile: "bg-orange-100 text-orange-600", arrow: "text-orange-600" },
  { card: "from-violet-50 to-white border-violet-100", tile: "bg-violet-100 text-violet-600", arrow: "text-violet-600" },
  { card: "from-cyan-50 to-white border-cyan-100", tile: "bg-cyan-100 text-cyan-600", arrow: "text-cyan-600" },
  { card: "from-rose-50 to-white border-rose-100", tile: "bg-rose-100 text-rose-600", arrow: "text-rose-600" },
];

export function categoryTone(id: number): CategoryTone {
  return TONES[Math.abs(id) % TONES.length];
}
