"use client";

import React from "react";
import { Badge } from "@/components/ui/badge";
import { Building2 } from "lucide-react";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";

// Re-export the type from admin-api for convenience
import type { GitHubInstallation } from "@/lib/admin-api";
export type { GitHubInstallation };

interface Department {
  id: string;
  name: string;
}

interface GitHubInstallationListProps {
  installations: GitHubInstallation[];
  selectedInstallation: GitHubInstallation | null;
  onSelect: (inst: GitHubInstallation) => void;
  /** Optional render function for extra actions per installation (e.g. disconnect button) */
  renderActions?: (inst: GitHubInstallation) => React.ReactNode;
  /** Available departments for linking. When provided, shows a department selector. */
  departments?: Department[];
  /** Callback when department link changes */
  onLinkDepartment?: (inst: GitHubInstallation, departmentId: string | null) => void;
}

export function GitHubInstallationList({
  installations,
  selectedInstallation,
  onSelect,
  renderActions,
  departments,
  onLinkDepartment,
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
            {departments && onLinkDepartment ? (
              <Select
                value={inst.departmentId || "__none__"}
                onValueChange={(value) => {
                  onLinkDepartment(inst, value === "__none__" ? null : value);
                }}
              >
                <SelectTrigger
                  className="h-7 w-[160px] text-xs"
                  onClick={(e) => e.stopPropagation()}
                >
                  <Building2 className="h-3 w-3 mr-1" />
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  <SelectItem value="__none__">No department</SelectItem>
                  {departments.map((d) => (
                    <SelectItem key={d.id} value={d.id}>
                      {d.name}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
            ) : inst.departmentName ? (
              <Badge variant="outline">
                <Building2 className="h-3 w-3 mr-1" />
                {inst.departmentName}
              </Badge>
            ) : null}
            {renderActions?.(inst)}
          </div>
        </div>
      ))}
    </div>
  );
}
