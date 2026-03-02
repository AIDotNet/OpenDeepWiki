"use client";

import { useEffect } from "react";
import { useRouter } from "next/navigation";

export default function PrivatePage() {
  const router = useRouter();
  useEffect(() => {
    router.replace("/?view=mine");
  }, [router]);
  return null;
}
