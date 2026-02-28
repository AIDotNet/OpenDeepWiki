"use client";

import React from "react";
import { Badge } from "@/components/ui/badge";
import { Building2 } from "lucide-react";

// Re-export the type from admin-api for convenience
import type { GitHubInstallation } from "@/lib/admin-api";
export type { GitHubInstallation };

interface GitHubInstallationListProps {
  installations: GitHubInstallation[];
  selectedInstallation: GitHubInstallation | null;
  onSelect: (inst: GitHubInstallation) => void;
  /** Optional render function for extra actions per installation (e.g. disconnect button) */
  renderActions?: (inst: GitHubInstallation) => React.ReactNode;
}

export function GitHubInstallationList({
  installations,
  selectedInstallation,
  onSelect,
  renderActions,
}: GitHubInstallationListProps) {
  return (
    <div className="grid gap-2">
      {installations.map((inst) => (
        <div
          key={inst.id}
          className={`flex items-center justify-between p-3 rounded-lg border cursor-pointer transition-colors ${
            selectedInstallation?.installationId === inst.installationId
              ? "border-primary bg-primary/5"
              : "hover:bg-muted"
          }`}
          onClick={() => onSelect(inst)}
        >
          <div className="flex items-center gap-3">
            {inst.avatarUrl && (
              <img
                src={inst.avatarUrl}
                alt={inst.accountLogin}
                className="h-8 w-8 rounded-full"
              />
            )}
            <div>
              <span className="font-medium">{inst.accountLogin}</span>
              <Badge variant="secondary" className="ml-2 text-xs">
                {inst.accountType}
              </Badge>
            </div>
          </div>
          <div className="flex items-center gap-2">
            {inst.departmentName && (
              <Badge variant="outline">
                <Building2 className="h-3 w-3 mr-1" />
                {inst.departmentName}
              </Badge>
            )}
            {renderActions?.(inst)}
          </div>
        </div>
      ))}
    </div>
  );
}
