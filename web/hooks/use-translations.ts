"use client";

import { useCallback } from 'react';
import { useTranslations as useNextIntlTranslations } from 'next-intl';

type TranslationValues = Record<string, string | number | boolean | Date | null | undefined>;

export function useTranslations() {
  const common = useNextIntlTranslations('common');
  const theme = useNextIntlTranslations('theme');
  const sidebar = useNextIntlTranslations('sidebar');
  const auth = useNextIntlTranslations('auth');
  const authUi = useNextIntlTranslations('authUi');
  const home = useNextIntlTranslations('home');
  const recommend = useNextIntlTranslations('recommend');
  const mindmap = useNextIntlTranslations('mindmap');
  const ui = useNextIntlTranslations('ui');
  const settings = useNextIntlTranslations('settings');
  const profile = useNextIntlTranslations('profile');
  const apps = useNextIntlTranslations('apps');
  const admin = useNextIntlTranslations('admin');
  const subscribe = useNextIntlTranslations('subscribe');

  // 使用 useCallback 缓存翻译函数，避免每次渲染创建新引用
  const t = useCallback((key: string, params?: TranslationValues): string => {
    const parts = key.split('.');
    
    if (parts.length < 2) {
      return key;
    }
    
    const namespace = parts[0];
    const translationKey = parts.slice(1).join('.');
    
    try {
      switch (namespace) {
        case 'common':
          // eslint-disable-next-line @typescript-eslint/no-explicit-any
          return common.raw(translationKey) ? common(translationKey as any, params as any) : key;
        case 'theme':
          // eslint-disable-next-line @typescript-eslint/no-explicit-any
          return theme.raw(translationKey) ? theme(translationKey as any, params as any) : key;
        case 'sidebar':
          // eslint-disable-next-line @typescript-eslint/no-explicit-any
          return sidebar.raw(translationKey) ? sidebar(translationKey as any, params as any) : key;
        case 'auth':
          // eslint-disable-next-line @typescript-eslint/no-explicit-any
          return auth.raw(translationKey) ? auth(translationKey as any, params as any) : key;
        case 'authUi':
          // eslint-disable-next-line @typescript-eslint/no-explicit-any
          return authUi.raw(translationKey) ? authUi(translationKey as any, params as any) : key;
        case 'home':
          // eslint-disable-next-line @typescript-eslint/no-explicit-any
          return home.raw(translationKey) ? home(translationKey as any, params as any) : key;
        case 'recommend':
          // eslint-disable-next-line @typescript-eslint/no-explicit-any
          return recommend.raw(translationKey) ? recommend(translationKey as any, params as any) : key;
        case 'mindmap':
          // eslint-disable-next-line @typescript-eslint/no-explicit-any
          return mindmap.raw(translationKey) ? mindmap(translationKey as any, params as any) : key;
        case 'ui':
          // eslint-disable-next-line @typescript-eslint/no-explicit-any
          return ui.raw(translationKey) ? ui(translationKey as any, params as any) : key;
        case 'settings':
          // eslint-disable-next-line @typescript-eslint/no-explicit-any
          return settings.raw(translationKey) ? settings(translationKey as any, params as any) : key;
        case 'profile':
          // eslint-disable-next-line @typescript-eslint/no-explicit-any
          return profile.raw(translationKey) ? profile(translationKey as any, params as any) : key;
        case 'apps':
          // eslint-disable-next-line @typescript-eslint/no-explicit-any
          return apps.raw(translationKey) ? apps(translationKey as any, params as any) : key;
        case 'admin':
          // eslint-disable-next-line @typescript-eslint/no-explicit-any
          return admin.raw(translationKey) ? admin(translationKey as any, params as any) : key;
        case 'subscribe':
          // eslint-disable-next-line @typescript-eslint/no-explicit-any
          return subscribe.raw(translationKey) ? subscribe(translationKey as any, params as any) : key;
        default:
          return key;
      }
    } catch (error) {
      console.error(`Translation error for key: ${key}`, error);
      return key;
    }
  }, [common, theme, sidebar, auth, authUi, home, recommend, mindmap, ui, settings, profile, apps, admin, subscribe]);

  return t;
}
