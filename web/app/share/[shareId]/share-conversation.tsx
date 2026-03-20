"use client"

import * as React from "react"
import Link from "next/link"
import { Copy, Check, MessageSquare } from "lucide-react"
import { ChatShareResponse, ChatShareMessage } from "@/lib/chat-api"
import { ChatMessageItem } from "@/components/chat"
import type { ChatMessage } from "@/hooks/use-chat-history"
import { Button } from "@/components/ui/button"
import { useTranslations } from "next-intl"

interface ShareConversationProps {
  share: ChatShareResponse
}

function mapShareMessage(message: ChatShareMessage): ChatMessage {
  return {
    id: message.id,
    role: message.role,
    content: message.content,
    thinking: message.thinking,
    contentBlocks: message.contentBlocks,
    images: message.images,
    quotedText: message.quotedText,
    toolCalls: message.toolCalls,
    toolResult: message.toolResult,
    tokenUsage: message.tokenUsage,
    timestamp: message.timestamp,
  }
}

export function ShareConversation({ share }: ShareConversationProps) {
  const [copied, setCopied] = React.useState(false)
  const t = useTranslations("chat")
  const common = useTranslations("common")
  const shareUrl = React.useMemo(() => {
    if (typeof window === "undefined") return `https://opendeep.wiki/share/${share.shareId}`
    return `${window.location.origin}/share/${share.shareId}`
  }, [share.shareId])

  const messages = React.useMemo(() => share.messages.map(mapShareMessage), [share.messages])

  const handleCopy = async () => {
    try {
      await navigator.clipboard.writeText(shareUrl)
      setCopied(true)
      setTimeout(() => setCopied(false), 1800)
    } catch {
      // ignore
    }
  }

  return (
    <div className="min-h-screen w-full bg-background">
      <main className="flex min-h-screen w-full flex-col">
        <header className="sticky top-0 z-10 border-b bg-background/95 backdrop-blur supports-[backdrop-filter]:bg-background/85">
          <div className="flex w-full items-center justify-between gap-3 px-4 py-2 md:px-6">
            <div className="min-w-0">
              <h1 className="truncate text-base font-semibold">
                {share.title || t("share.title")}
              </h1>
              <p className="mt-0.5 inline-flex items-center gap-1.5 text-xs text-muted-foreground">
                <MessageSquare className="h-3.5 w-3.5" />
                {t("share.messageCount", { count: messages.length })}
              </p>
            </div>
            <div className="flex shrink-0 items-center gap-2">
              <Button variant="outline" size="sm" className="gap-2" onClick={handleCopy}>
                {copied ? (
                  <>
                    <Check className="h-4 w-4" />
                    {t("panel.copied")}
                  </>
                ) : (
                  <>
                    <Copy className="h-4 w-4" />
                    {t("panel.copyLink")}
                  </>
                )}
              </Button>
              <Button variant="ghost" size="sm" asChild>
                <Link href="/">{common("backToHome")}</Link>
              </Button>
            </div>
          </div>
        </header>

        <section className="flex-1">
          {messages.length === 0 ? (
            <div className="px-4 py-6 text-center text-muted-foreground md:px-6">{t("share.empty")}</div>
          ) : (
            <div className="w-full px-2 py-1 md:px-4 md:py-2 lg:px-6">
              {messages.map((message) => (
                <div key={message.id} className="py-0.5">
                  <ChatMessageItem message={message} />
                </div>
              ))}
            </div>
          )}
        </section>
      </main>
    </div>
  )
}
