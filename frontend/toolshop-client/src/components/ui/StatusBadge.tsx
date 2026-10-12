type Tone = "green" | "red" | "yellow" | "blue" | "gray";

const tones: Record<Tone, string> = {
  green: "bg-green-100 text-green-700",
  red: "bg-red-100 text-red-700",
  yellow: "bg-amber-100 text-amber-700",
  blue: "bg-blue-100 text-blue-700",
  gray: "bg-slate-100 text-slate-600",
};

export default function StatusBadge({ label, tone = "gray" }: { label: string; tone?: Tone }) {
  return (
    <span className={`inline-flex items-center rounded-full px-2.5 py-0.5 text-xs font-medium ${tones[tone]}`}>
      {label}
    </span>
  );
}
