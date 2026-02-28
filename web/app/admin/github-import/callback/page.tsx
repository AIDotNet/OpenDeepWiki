"use client";

import { useEffect, useState } from "react";
import { useRouter, useSearchParams } from "next/navigation";
import { storeGitHubInstallation } from "@/lib/admin-api";
import { Loader2 } from "lucide-react";

export default function GitHubImportCallbackPage() {
  const router = useRouter();
  const searchParams = useSearchParams();
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    const installationId = searchParams.get("installation_id");
    const setupAction = searchParams.get("setup_action");

    if (!installationId) {
      setError("Missing installation_id parameter");
      return;
    }

    const storeAndRedirect = async () => {
      try {
        await storeGitHubInstallation(parseInt(installationId, 10));
        router.push("/admin/github-import");
      } catch (err) {
        setError(err instanceof Error ? err.message : "Failed to store installation");
      }
    };

    storeAndRedirect();
  }, [searchParams, router]);

  if (error) {
    return (
      <div className="flex flex-col items-center justify-center min-h-[400px] gap-4">
        <p className="text-destructive text-lg">Error: {error}</p>
        <button
          onClick={() => router.push("/admin/github-import")}
          className="text-primary underline"
        >
          Back to GitHub Import
        </button>
      </div>
    );
  }

  return (
    <div className="flex flex-col items-center justify-center min-h-[400px] gap-4">
      <Loader2 className="h-8 w-8 animate-spin text-primary" />
      <p className="text-muted-foreground">Connecting GitHub installation...</p>
    </div>
  );
}
