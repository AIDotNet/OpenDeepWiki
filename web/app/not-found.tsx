import { getTranslations } from "next-intl/server";

export default async function NotFound() {
  const t = await getTranslations("common");

  return (
    <main className="flex min-h-[60vh] flex-col items-center justify-center px-6 text-center">
      <h1 className="text-3xl font-semibold">{t("repository.pageNotFound.title")}</h1>
      <p className="mt-3 text-sm text-muted-foreground">
        {t("repository.pageNotFound.description")}
      </p>
      <a
        href="/"
        className="mt-6 inline-flex h-10 items-center justify-center rounded-md bg-primary px-4 text-sm font-medium text-primary-foreground transition-colors hover:bg-primary/90"
      >
        {t("backToHome")}
      </a>
    </main>
  );
}
