"use client";

import React, { useEffect, useState, useCallback } from "react";
import { Card } from "@/components/ui/card";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuTrigger,
} from "@/components/ui/dropdown-menu";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";
import {
  Dialog,
  DialogContent,
  DialogHeader,
  DialogTitle,
  DialogFooter,
} from "@/components/ui/dialog";
import {
  AlertDialog,
  AlertDialogAction,
  AlertDialogCancel,
  AlertDialogContent,
  AlertDialogDescription,
  AlertDialogFooter,
  AlertDialogHeader,
  AlertDialogTitle,
} from "@/components/ui/alert-dialog";
import { Checkbox } from "@/components/ui/checkbox";
import {
  getUsers,
  getRoles,
  createUser,
  deleteUser,
  updateUserStatus,
  updateUserRoles,
  resetUserPassword,
  AdminUser,
  AdminRole,
  UserListResponse,
} from "@/lib/admin-api";
import {
  Loader2,
  Search,
  Trash2,
  Edit,
  RefreshCw,
  ChevronLeft,
  ChevronRight,
  Plus,
  Key,
  Shield,
  ChevronDown,
  Check,
} from "lucide-react";
import { Avatar, AvatarFallback, AvatarImage } from "@/components/ui/avatar";
import { toast } from "sonner";
import { useTranslations } from "@/hooks/use-translations";
import { useLocale } from "next-intl";

export default function AdminUsersPage() {
  const [data, setData] = useState<UserListResponse | null>(null);
  const t = useTranslations();
  const locale = useLocale();
  const [roles, setRoles] = useState<AdminRole[]>([]);
  const [loading, setLoading] = useState(true);
  const [statusUpdatingId, setStatusUpdatingId] = useState<string | null>(null);
  const [page, setPage] = useState(1);
  const [search, setSearch] = useState("");
  const [roleFilter, setRoleFilter] = useState("all");
  
  const [selectedUser, setSelectedUser] = useState<AdminUser | null>(null);
  const [deleteId, setDeleteId] = useState<string | null>(null);
  const [showCreateDialog, setShowCreateDialog] = useState(false);
  const [showRolesDialog, setShowRolesDialog] = useState<AdminUser | null>(null);
  const [showPasswordDialog, setShowPasswordDialog] = useState<AdminUser | null>(null);
  
  const [newUser, setNewUser] = useState({ name: "", email: "", password: "" });
  const [selectedRoles, setSelectedRoles] = useState<string[]>([]);
  const [newPassword, setNewPassword] = useState("");

  const fetchData = useCallback(async () => {
    setLoading(true);
    try {
      const [usersResult, rolesResult] = await Promise.all([
        getUsers(page, 20, search || undefined, roleFilter === "all" ? undefined : roleFilter),
        getRoles(),
      ]);
      setData(usersResult);
      setRoles(rolesResult);
    } catch (error) {
      console.error("Failed to fetch users:", error);
      toast.error(t('admin.toast.fetchUserFailed'));
    } finally {
      setLoading(false);
    }
  }, [page, search, roleFilter]);

  useEffect(() => {
    fetchData();
  }, [fetchData]);

  const handleSearch = () => {
    setPage(1);
    fetchData();
  };

  const handleCreate = async () => {
    if (!newUser.name || !newUser.email || !newUser.password) {
      toast.error(t('admin.users.fillComplete'));
      return;
    }
    try {
      await createUser(newUser);
      toast.success(t('admin.toast.createSuccess'));
      setShowCreateDialog(false);
      setNewUser({ name: "", email: "", password: "" });
      fetchData();
    } catch (error) {
      toast.error(t('admin.toast.createFailed'));
    }
  };

  const handleDelete = async () => {
    if (!deleteId) return;
    try {
      await deleteUser(deleteId);
      toast.success(t('admin.toast.deleteSuccess'));
      setDeleteId(null);
      fetchData();
    } catch (error) {
      toast.error(t('admin.toast.deleteFailed'));
    }
  };

  const handleStatusChange = async (id: string, newStatus: number, currentStatus?: number) => {
    if (currentStatus === newStatus) return;
    setStatusUpdatingId(id);
    try {
      await updateUserStatus(id, newStatus);
      toast.success(t('admin.toast.statusUpdateSuccess'));
      fetchData();
    } catch (error) {
      toast.error(t('admin.toast.statusUpdateFailed'));
    } finally {
      setStatusUpdatingId((prev) => (prev === id ? null : prev));
    }
  };

  const handleRolesUpdate = async () => {
    if (!showRolesDialog) return;
    try {
      await updateUserRoles(showRolesDialog.id, selectedRoles);
      toast.success(t('admin.toast.roleUpdateSuccess'));
      setShowRolesDialog(null);
      fetchData();
    } catch (error) {
      toast.error(t('admin.toast.roleUpdateFailed'));
    }
  };

  const handlePasswordReset = async () => {
    if (!showPasswordDialog || !newPassword) return;
    try {
      await resetUserPassword(showPasswordDialog.id, newPassword);
      toast.success(t('admin.toast.passwordResetSuccess'));
      setShowPasswordDialog(null);
      setNewPassword("");
    } catch (error) {
      toast.error(t('admin.toast.passwordResetFailed'));
    }
  };

  const openRolesDialog = (user: AdminUser) => {
    // Map role names to IDs since the API expects IDs
    const roleIds = (user.roles || []).map(
      (name) => roles.find((r) => r.name === name)?.id
    ).filter(Boolean) as string[];
    setSelectedRoles(roleIds);
    setShowRolesDialog(user);
  };

  const totalPages = data ? Math.ceil(data.total / data.pageSize) : 0;
  const userStatusLabels: Record<number, string> = {
    1: t('admin.users.normal'),
    0: t('admin.users.disabled'),
  };
  const userStatusColors: Record<number, string> = {
    1: "bg-green-100 text-green-800 dark:bg-green-900 dark:text-green-200",
    0: "bg-red-100 text-red-800 dark:bg-red-900 dark:text-red-200",
  };
  const userStatusDots: Record<number, string> = {
    1: "bg-green-500",
    0: "bg-red-500",
  };

  return (
    <div className="space-y-6">
      <div className="flex items-center justify-between">
        <h1 className="text-2xl font-bold">{t('admin.users.title')}</h1>
        <div className="flex gap-2">
          <Button variant="outline" onClick={fetchData}>
            <RefreshCw className="mr-2 h-4 w-4" />
            {t('admin.common.refresh')}
          </Button>
          <Button onClick={() => setShowCreateDialog(true)}>
            <Plus className="mr-2 h-4 w-4" />
            {t('admin.users.createUser')}
          </Button>
        </div>
      </div>

      {/* 搜索和筛选 */}
      <Card className="p-4">
        <div className="flex flex-wrap gap-4">
          <div className="flex flex-1 gap-2">
            <Input
              placeholder={t('admin.users.searchPlaceholder')}
              value={search}
              onChange={(e) => setSearch(e.target.value)}
              onKeyDown={(e) => e.key === "Enter" && handleSearch()}
              className="max-w-md"
            />
            <Button onClick={handleSearch}>
              <Search className="mr-2 h-4 w-4" />
              {t('admin.common.search')}
            </Button>
          </div>
          <Select value={roleFilter} onValueChange={(v) => { setRoleFilter(v); setPage(1); }}>
            <SelectTrigger className="w-[150px]">
              <SelectValue placeholder={t('admin.users.filterRole')} />
            </SelectTrigger>
            <SelectContent>
              <SelectItem value="all">{t('admin.users.allRoles')}</SelectItem>
              {roles.map((role) => (
                <SelectItem key={role.id} value={role.id}>
                  {role.name}
                </SelectItem>
              ))}
            </SelectContent>
          </Select>
        </div>
      </Card>

      {/* 用户列表 */}
      <Card>
        {loading ? (
          <div className="flex h-64 items-center justify-center">
            <Loader2 className="h-8 w-8 animate-spin text-muted-foreground" />
          </div>
        ) : (
          <>
            <div className="overflow-x-auto">
              <table className="w-full">
                <thead className="border-b bg-muted/50">
                  <tr>
                    <th className="px-4 py-3 text-left text-sm font-medium">{t('admin.users.user')}</th>
                    <th className="px-4 py-3 text-left text-sm font-medium">{t('admin.users.email')}</th>
                    <th className="px-4 py-3 text-left text-sm font-medium">{t('admin.users.role')}</th>
                    <th className="px-4 py-3 text-left text-sm font-medium">{t('admin.users.status')}</th>
                    <th className="px-4 py-3 text-left text-sm font-medium">{t('admin.users.createdAt')}</th>
                    <th className="px-4 py-3 text-right text-sm font-medium">{t('admin.users.operations')}</th>
                  </tr>
                </thead>
                <tbody className="divide-y">
                  {data?.items.map((user) => (
                    <tr key={user.id} className="hover:bg-muted/50">
                      <td className="px-4 py-3">
                        <div className="flex items-center gap-3">
                          <Avatar className="h-8 w-8">
                            <AvatarImage src={user.avatar} />
                            <AvatarFallback>{user.name.charAt(0).toUpperCase()}</AvatarFallback>
                          </Avatar>
                          <span className="font-medium">{user.name}</span>
                        </div>
                      </td>
                      <td className="px-4 py-3 text-sm text-muted-foreground">
                        {user.email || "-"}
                      </td>
                      <td className="px-4 py-3">
                        <div className="flex flex-wrap gap-1">
                          {user.roles?.map((role) => (
                            <span
                              key={role}
                              className="inline-flex items-center rounded-full bg-primary/10 px-2 py-1 text-xs font-medium text-primary"
                            >
                              {role}
                            </span>
                          ))}
                        </div>
                      </td>
                      <td className="px-4 py-3">
                        <DropdownMenu>
                          <DropdownMenuTrigger asChild>
                            <Button
                              variant="outline"
                              size="sm"
                              disabled={statusUpdatingId === user.id}
                              className="h-8 w-[112px] justify-between px-2"
                            >
                              <span className={`inline-flex items-center gap-1 rounded px-2 py-0.5 text-xs ${userStatusColors[user.status]}`}>
                                <span className="h-1.5 w-1.5 rounded-full bg-current/80" />
                                {userStatusLabels[user.status]}
                              </span>
                              {statusUpdatingId === user.id ? (
                                <Loader2 className="h-3.5 w-3.5 animate-spin text-muted-foreground" />
                              ) : (
                                <ChevronDown className="h-3.5 w-3.5 text-muted-foreground" />
                              )}
                            </Button>
                          </DropdownMenuTrigger>
                          <DropdownMenuContent align="start" className="w-[150px]">
                            {[1, 0].map((statusValue) => (
                              <DropdownMenuItem
                                key={statusValue}
                                disabled={user.status === statusValue}
                                onClick={() => handleStatusChange(user.id, statusValue, user.status)}
                                className="justify-between"
                              >
                                <span className="inline-flex items-center gap-2">
                                  <span className={`h-2 w-2 rounded-full ${userStatusDots[statusValue]}`} />
                                  {userStatusLabels[statusValue]}
                                </span>
                                {user.status === statusValue ? <Check className="h-3.5 w-3.5 text-primary" /> : null}
                              </DropdownMenuItem>
                            ))}
                          </DropdownMenuContent>
                        </DropdownMenu>
                      </td>
                      <td className="px-4 py-3 text-sm text-muted-foreground">
                        {new Date(user.createdAt).toLocaleDateString(locale === 'zh' ? 'zh-CN' : locale)}
                      </td>
                      <td className="px-4 py-3 text-right">
                        <div className="flex justify-end gap-1">
                          <Button
                            variant="ghost"
                            size="icon"
                            title={t('admin.users.assignRoles')}
                            onClick={() => openRolesDialog(user)}
                          >
                            <Shield className="h-4 w-4" />
                          </Button>
                          <Button
                            variant="ghost"
                            size="icon"
                            title={t('admin.users.resetPassword')}
                            onClick={() => setShowPasswordDialog(user)}
                          >
                            <Key className="h-4 w-4" />
                          </Button>
                          <Button
                            variant="ghost"
                            size="icon"
                            title={t('admin.common.delete')}
                            onClick={() => setDeleteId(user.id)}
                          >
                            <Trash2 className="h-4 w-4 text-red-500" />
                          </Button>
                        </div>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>

            {/* 分页 */}
            {totalPages > 1 && (
              <div className="flex items-center justify-between border-t px-4 py-3">
                <p className="text-sm text-muted-foreground">{t('admin.repositories.totalRecords', { count: data?.total })}</p>
                <div className="flex items-center gap-2">
                  <Button variant="outline" size="sm" disabled={page === 1} onClick={() => setPage(page - 1)}>
                    <ChevronLeft className="h-4 w-4" />
                  </Button>
                  <span className="text-sm">{page} / {totalPages}</span>
                  <Button variant="outline" size="sm" disabled={page === totalPages} onClick={() => setPage(page + 1)}>
                    <ChevronRight className="h-4 w-4" />
                  </Button>
                </div>
              </div>
            )}
          </>
        )}
      </Card>

      {/* 新增用户对话框 */}
      <Dialog open={showCreateDialog} onOpenChange={setShowCreateDialog}>
        <DialogContent>
          <DialogHeader>
            <DialogTitle>{t('admin.users.createUser')}</DialogTitle>
          </DialogHeader>
          <div className="space-y-4">
            <div>
              <label className="text-sm font-medium">{t('admin.users.username')} *</label>
              <Input
                value={newUser.name}
                onChange={(e) => setNewUser({ ...newUser, name: e.target.value })}
                placeholder={t('admin.users.enterUsername')}
              />
            </div>
            <div>
              <label className="text-sm font-medium">{t('admin.users.email')} *</label>
              <Input
                type="email"
                value={newUser.email}
                onChange={(e) => setNewUser({ ...newUser, email: e.target.value })}
                placeholder={t('admin.users.enterEmail')}
              />
            </div>
            <div>
              <label className="text-sm font-medium">{t('admin.users.password')} *</label>
              <Input
                type="password"
                value={newUser.password}
                onChange={(e) => setNewUser({ ...newUser, password: e.target.value })}
                placeholder={t('admin.users.enterPassword')}
              />
            </div>
          </div>
          <DialogFooter>
            <Button variant="outline" onClick={() => setShowCreateDialog(false)}>{t('admin.common.cancel')}</Button>
            <Button onClick={handleCreate}>{t('admin.common.create')}</Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>

      {/* 角色分配对话框 */}
      <Dialog open={!!showRolesDialog} onOpenChange={() => setShowRolesDialog(null)}>
        <DialogContent>
          <DialogHeader>
            <DialogTitle>{t('admin.users.assignRoles')} - {showRolesDialog?.name}</DialogTitle>
          </DialogHeader>
          <div className="space-y-3">
            {roles.map((role) => (
              <div key={role.id} className="flex items-center gap-2">
                <Checkbox
                  id={role.id}
                  checked={selectedRoles.includes(role.id)}
                  onCheckedChange={(checked) => {
                    if (checked) {
                      setSelectedRoles([...selectedRoles, role.id]);
                    } else {
                      setSelectedRoles(selectedRoles.filter((r) => r !== role.id));
                    }
                  }}
                />
                <label htmlFor={role.id} className="text-sm">
                  {role.name}
                  {role.description && (
                    <span className="ml-2 text-muted-foreground">({role.description})</span>
                  )}
                </label>
              </div>
            ))}
          </div>
          <DialogFooter>
            <Button variant="outline" onClick={() => setShowRolesDialog(null)}>{t('admin.common.cancel')}</Button>
            <Button onClick={handleRolesUpdate}>{t('admin.common.save')}</Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>

      {/* 重置密码对话框 */}
      <Dialog open={!!showPasswordDialog} onOpenChange={() => setShowPasswordDialog(null)}>
        <DialogContent>
          <DialogHeader>
            <DialogTitle>{t('admin.users.resetPassword')} - {showPasswordDialog?.name}</DialogTitle>
          </DialogHeader>
          <div>
            <label className="text-sm font-medium">{t('admin.users.newPassword')}</label>
            <Input
              type="password"
              value={newPassword}
              onChange={(e) => setNewPassword(e.target.value)}
              placeholder={t('admin.users.enterNewPassword')}
            />
          </div>
          <DialogFooter>
            <Button variant="outline" onClick={() => setShowPasswordDialog(null)}>{t('admin.common.cancel')}</Button>
            <Button onClick={handlePasswordReset}>{t('admin.users.confirmReset')}</Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>

      {/* 删除确认对话框 */}
      <AlertDialog open={!!deleteId} onOpenChange={() => setDeleteId(null)}>
        <AlertDialogContent>
          <AlertDialogHeader>
            <AlertDialogTitle>{t('admin.common.confirmDelete')}</AlertDialogTitle>
            <AlertDialogDescription>
              {t('admin.users.deleteWarning')}
            </AlertDialogDescription>
          </AlertDialogHeader>
          <AlertDialogFooter>
            <AlertDialogCancel>{t('admin.common.cancel')}</AlertDialogCancel>
            <AlertDialogAction onClick={handleDelete} className="bg-red-600 hover:bg-red-700">
              {t('admin.common.delete')}
            </AlertDialogAction>
          </AlertDialogFooter>
        </AlertDialogContent>
      </AlertDialog>
    </div>
  );
}
