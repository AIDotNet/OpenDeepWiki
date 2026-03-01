import { getRequestConfig } from 'next-intl/server';

export const locales = ['zh', 'en', 'ko', 'ja'] as const;
export type Locale = (typeof locales)[number];

export const localeNames: Record<Locale, string> = {
  zh: '简体中文',
  en: 'English',
  ko: '한국어',
  ja: '日本語',
};

// 动态加载所有翻译文件
async function loadMessages(locale: Locale) {
  const common = (await import(`./messages/${locale}/common.json`)).default;
  const theme = (await import(`./messages/${locale}/theme.json`)).default;
  const sidebar = (await import(`./messages/${locale}/sidebar.json`)).default;
  const auth = (await import(`./messages/${locale}/auth.json`)).default;
  const authUi = (await import(`./messages/${locale}/auth-ui.json`)).default;
  const home = (await import(`./messages/${locale}/home.json`)).default;
  const ui = (await import(`./messages/${locale}/ui.json`)).default;
  const recommend = (await import(`./messages/${locale}/recommend.json`)).default;
  const mindmap = (await import(`./messages/${locale}/mindmap.json`)).default;
  const settings = (await import(`./messages/${locale}/settings.json`)).default;
  const profile = (await import(`./messages/${locale}/profile.json`)).default;
  const apps = (await import(`./messages/${locale}/apps.json`)).default;
  const admin = (await import(`./messages/${locale}/admin.json`)).default;
  const chat = (await import(`./messages/${locale}/chat.json`)).default;
  const subscribe = (await import(`./messages/${locale}/subscribe.json`)).default;

  return {
    common,
    theme,
    sidebar,
    auth,
    authUi,
    home,
    ui,
    recommend,
    mindmap,
    settings,
    profile,
    apps,
    admin,
    chat,
    subscribe,
  };
}

export default getRequestConfig(async ({ requestLocale }) => {
  // 从 requestLocale 获取 locale，如果没有则使用默认值
  let locale = await requestLocale;
  
  if (!locale || !locales.includes(locale as Locale)) {
    locale = 'zh';
  }

  return {
    locale,
    messages: await loadMessages(locale as Locale),
  };
});
