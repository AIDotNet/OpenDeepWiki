import type { RepoHeading } from "@/types/repository";

export interface FumadocsTocItem {
  title: string;
  url: string;
  depth: number;
  items?: FumadocsTocItem[];
}

export function convertToFumadocsToc(headings: RepoHeading[]): FumadocsTocItem[] {
  return headings.map((h) => ({
    title: h.text,
    url: `#${h.id}`,
    depth: h.level,
  }));
}

export function nestTocItems(items: FumadocsTocItem[]): FumadocsTocItem[] {
  const result: FumadocsTocItem[] = [];
  const stack: FumadocsTocItem[] = [];

  for (const item of items) {
    while (stack.length > 0 && stack[stack.length - 1].depth >= item.depth) {
      stack.pop();
    }

    if (stack.length === 0) {
      result.push(item);
    } else {
      const parent = stack[stack.length - 1];
      if (!parent.items) {
        parent.items = [];
      }
      parent.items.push(item);
    }

    stack.push(item);
  }

  return result;
}