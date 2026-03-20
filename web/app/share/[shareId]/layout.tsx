import type { Metadata } from "next"
import "@/app/globals.css"

export const metadata: Metadata = {
  title: "AI Chat Share",
}

export default function ShareLayout({ children }: { children: React.ReactNode }) {
  return (
    <div className="min-h-screen bg-slate-950 text-white">
      <div className="pointer-events-none fixed inset-0 -z-10">
        <div className="absolute inset-0 bg-[radial-gradient(circle_at_top,_rgba(59,130,246,0.25),_transparent_55%)]" />
        <div className="absolute inset-0 bg-[radial-gradient(circle_at_bottom,_rgba(236,72,153,0.2),_transparent_60%)]" />
        <div className="absolute inset-0 bg-slate-950/80 backdrop-blur" />
      </div>
      <main >
        {children}
      </main>
    </div>
  )
}
