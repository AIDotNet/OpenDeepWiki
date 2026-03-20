"use client"

import * as React from "react"
import { useTranslations } from "next-intl"
import { Send, Loader2, X, ImagePlus, Trash2, RefreshCw, GripVertical, Share2, Copy, Check } from "lucide-react"
import { cn } from "@/lib/utils"
import { Button } from "@/components/ui/button"
import { Textarea } from "@/components/ui/textarea"
import { ScrollArea } from "@/components/ui/scroll-area"
import { useChatHistory } from "@/hooks/use-chat-history"
import { useLocale } from "next-intl"
import {
  streamChat,
  getAvailableModels,
  getChatConfig,
  createChatShare,
  toChatMessageDto,
  toChatShareMessage,
  DocContext,
  ModelConfig,
  ToolCall,
  ToolResult,
  ErrorInfo,
  ChatErrorCodes,
  getErrorMessage,
  isRetryableError,
  ThinkingEvent,
  ToolCallEvent,
  ChatShareResponse,
} from "@/lib/chat-api"
import { ContentBlock } from "@/hooks/use-chat-history"
import { ModelSelector } from "./model-selector"
import { ChatMessageItem } from "./chat-message"
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from "@/components/ui/dialog"
import { Input } from "@/components/ui/input"

const MIN_WIDTH = 320
const MAX_WIDTH = 800
const DEFAULT_WIDTH = 420
const STORAGE_KEY = "chat-panel-width"

/**
 * 对话面板属性
 */
export interface ChatPanelProps {
  /** 是否展开 */
  isOpen: boolean
  /** 关闭回调 */
  onClose: () => void
  /** 文档上下文 */
  context: DocContext
  /** 应用ID（嵌入模式） */
  appId?: string
}

/**
 * 错误状态
 */
interface ErrorState {
  message: string
  code?: string
  retryable: boolean
  retryAfterMs?: number
}

/**
 * 对话面板组件
 * 
 * 包含消息列表、输入框、发送按钮、模型选择器
 * 支持Markdown渲染、工具调用显示、错误处理和重试
 * 
 * Requirements: 2.1, 2.2, 2.3, 2.5, 2.6, 11.1, 11.2, 11.3, 11.4
 */
export function ChatPanel({
  isOpen,
  onClose,
  context,
  appId,
}: ChatPanelProps) {
  const locale = useLocale()
  const t = useTranslations("chat")
  const common = useTranslations("common")
  const { messages, addMessage, updateMessage, clearHistory } = useChatHistory()
  const [input, setInput] = React.useState("")
  const [images, setImages] = React.useState<string[]>([])
  const [isLoading, setIsLoading] = React.useState(false)
  const [models, setModels] = React.useState<ModelConfig[]>([])
  const [selectedModelId, setSelectedModelId] = React.useState("")
  const [isEnabled, setIsEnabled] = React.useState(true)
  const [enableImageUpload, setEnableImageUpload] = React.useState(false)
  const [error, setError] = React.useState<ErrorState | null>(null)
  const [isShareDialogOpen, setIsShareDialogOpen] = React.useState(false)
  const [shareTitle, setShareTitle] = React.useState("")
  const [shareDescription, setShareDescription] = React.useState("")
  const [shareExpireMinutes, setShareExpireMinutes] = React.useState(60 * 24 * 7)
  const [shareLoading, setShareLoading] = React.useState(false)
  const [shareResult, setShareResult] = React.useState<ChatShareResponse | null>(null)
  const [shareError, setShareError] = React.useState<string | null>(null)
  const [shareCopied, setShareCopied] = React.useState(false)
  // 引用的选中文本（包含标题）
  const [quotedText, setQuotedText] = React.useState<{ title?: string; text: string } | null>(null)
  const [lastRequest, setLastRequest] = React.useState<{
    input: string
    images: string[]
    userMessageId: string
    assistantMessageId: string
  } | null>(null)
  
  // 面板宽度状态
  const [panelWidth, setPanelWidth] = React.useState(DEFAULT_WIDTH)
  const panelRef = React.useRef<HTMLDivElement>(null)
  const isDraggingRef = React.useRef(false)
  const rafRef = React.useRef<number | null>(null)
  const messagesEndRef = React.useRef<HTMLDivElement>(null)
  const inputRef = React.useRef<HTMLTextAreaElement>(null)
  const fileInputRef = React.useRef<HTMLInputElement>(null)
  const abortControllerRef = React.useRef<AbortController | null>(null)

  const resetShareState = React.useCallback(() => {
    setShareDescription("")
    setShareResult(null)
    setShareError(null)
    setShareCopied(false)
    setShareLoading(false)
  }, [])

  const getSuggestedShareTitle = React.useCallback(() => {
    const lastUserMessage = [...messages].reverse().find(m => m.role === "user" && m.content?.trim())
    if (lastUserMessage?.content) {
      const trimmed = lastUserMessage.content.trim()
      return trimmed.length > 40 ? `${trimmed.slice(0, 40)}…` : trimmed
    }
    return context.currentDocPath || t("assistant.title")
  }, [messages, context.currentDocPath, t])

  const handleOpenShareDialog = React.useCallback(() => {
    if (messages.length === 0) return
    resetShareState()
    setShareTitle(getSuggestedShareTitle())
    setShareExpireMinutes(60 * 24 * 7)
    setIsShareDialogOpen(true)
  }, [messages.length, resetShareState, getSuggestedShareTitle])

  const handleShareDialogChange = React.useCallback((open: boolean) => {
    setIsShareDialogOpen(open)
    if (!open) {
      resetShareState()
    }
  }, [resetShareState])

  const shareLink = React.useMemo(() => {
    if (!shareResult) return ""
    if (typeof window === "undefined") {
      return `https://opendeepwiki.com/share/${shareResult.shareId}`
    }
    return `${window.location.origin}/share/${shareResult.shareId}`
  }, [shareResult])

  const handleCopyShareLink = React.useCallback(async () => {
    if (!shareLink) return
    try {
      await navigator.clipboard.writeText(shareLink)
      setShareCopied(true)
      setTimeout(() => setShareCopied(false), 1800)
    } catch {
      // ignore
    }
  }, [shareLink])

  const handleCreateShare = React.useCallback(async () => {
    if (messages.length === 0) {
      setShareError(t("panel.shareNoMessages"))
      return
    }

    const shareModelId = selectedModelId || models.find(m => m.isEnabled)?.id || models[0]?.id
    if (!shareModelId) {
      setShareError(t("panel.shareSelectModel"))
      return
    }

    setShareLoading(true)
    setShareError(null)
    try {
      const payload = {
        messages: messages.map(toChatShareMessage),
        context: {
          ...context,
          userLanguage: locale,
        },
        modelId: shareModelId,
        title: shareTitle.trim() || undefined,
        description: shareDescription.trim() || undefined,
        expireMinutes: shareExpireMinutes,
      }
      const result = await createChatShare(payload)
      setShareResult(result)
    } catch (err) {
      setShareError(err instanceof Error ? err.message : t("panel.shareFailed"))
    } finally {
      setShareLoading(false)
    }
  }, [messages, selectedModelId, models, context, locale, shareTitle, shareDescription, shareExpireMinutes])

  // 从 localStorage 加载保存的宽度
  React.useEffect(() => {
    const saved = localStorage.getItem(STORAGE_KEY)
    if (saved) {
      const width = parseInt(saved, 10)
      if (width >= MIN_WIDTH && width <= MAX_WIDTH) {
        setPanelWidth(width)
      }
    }
  }, [])

  // 拖动调整宽度
  const handleResizeStart = React.useCallback((e: React.MouseEvent) => {
    e.preventDefault()
    isDraggingRef.current = true
    document.body.style.cursor = "col-resize"
    document.body.style.userSelect = "none"
  }, [])

  React.useEffect(() => {
    const handleMouseMove = (e: MouseEvent) => {
      if (!isDraggingRef.current) return
      
      if (rafRef.current) {
        cancelAnimationFrame(rafRef.current)
      }
      
      rafRef.current = requestAnimationFrame(() => {
        const newWidth = window.innerWidth - e.clientX
        const clampedWidth = Math.min(MAX_WIDTH, Math.max(MIN_WIDTH, newWidth))
        setPanelWidth(clampedWidth)
      })
    }

    const handleMouseUp = () => {
      if (isDraggingRef.current) {
        isDraggingRef.current = false
        document.body.style.cursor = ""
        document.body.style.userSelect = ""
        // 保存到 localStorage
        localStorage.setItem(STORAGE_KEY, panelWidth.toString())
      }
      if (rafRef.current) {
        cancelAnimationFrame(rafRef.current)
        rafRef.current = null
      }
    }

    document.addEventListener("mousemove", handleMouseMove)
    document.addEventListener("mouseup", handleMouseUp)

    return () => {
      document.removeEventListener("mousemove", handleMouseMove)
      document.removeEventListener("mouseup", handleMouseUp)
      if (rafRef.current) {
        cancelAnimationFrame(rafRef.current)
      }
    }
  }, [panelWidth])

  // 加载配置和模型列表
  React.useEffect(() => {
    if (!isOpen) return

    const loadConfig = async () => {
      try {
        const [config, modelList] = await Promise.all([
          getChatConfig(),
          getAvailableModels(),
        ])
        setIsEnabled(config.isEnabled)
        setEnableImageUpload(config.enableImageUpload)
        setModels(modelList)
        
        // 设置默认模型
        if (config.defaultModelId) {
          setSelectedModelId(config.defaultModelId)
        } else if (modelList.length > 0) {
          const enabledModel = modelList.find(m => m.isEnabled)
          if (enabledModel) {
            setSelectedModelId(enabledModel.id)
          }
        }
      } catch (err) {
        console.error(t("error.loadConfigFailed"), err)
        setError({
          message: t("assistant.loadConfigFailed"),
          code: ChatErrorCodes.CONFIG_MISSING,
          retryable: true,
        })
      }
    }

    loadConfig()
  }, [isOpen])

  // 组件卸载时取消请求
  React.useEffect(() => {
    return () => {
      if (abortControllerRef.current) {
        abortControllerRef.current.abort()
      }
    }
  }, [])

  // 滚动到底部的函数
  const scrollToBottom = React.useCallback((smooth = true) => {
    if (messagesEndRef.current) {
      messagesEndRef.current.scrollIntoView({ 
        behavior: smooth ? "smooth" : "instant",
        block: "end" 
      })
    }
  }, [])

  // 消息变化时滚动到底部
  React.useEffect(() => {
    // 使用 requestAnimationFrame 确保 DOM 更新后再滚动
    requestAnimationFrame(() => {
      scrollToBottom()
    })
  }, [messages, scrollToBottom])

  // AI 回复时持续滚动到底部
  React.useEffect(() => {
    if (isLoading) {
      const interval = setInterval(() => scrollToBottom(false), 100)
      return () => clearInterval(interval)
    }
  }, [isLoading, scrollToBottom])

  // 监听用户选中文本（只在文档内容区域）
  React.useEffect(() => {
    if (!isOpen) return

    const handleSelectionChange = () => {
      const selection = window.getSelection()
      const text = selection?.toString().trim()
      
      // 如果没有选中文本，清除引用
      if (!text) {
        setQuotedText(null)
        return
      }
      
      const anchorNode = selection?.anchorNode
      if (!anchorNode) return
      
      // 检查选中的文本是否在文档内容区域
      const docContentSelectors = [
        '[data-doc-content]',
        '.prose',
        '.markdown-body',
        'article',
        'main',
      ]
      
      const parentElement = anchorNode.parentElement
      if (!parentElement) return
      
      // 检查是否在文档内容区域内
      const isInDocContent = docContentSelectors.some(selector => 
        parentElement.closest(selector) !== null
      )
      
      // 排除在对话面板内选中的文本
      const isInChatPanel = panelRef.current?.contains(anchorNode as Node)
      
      if (isInDocContent && !isInChatPanel) {
        const title = context.currentDocPath || document.title
        setQuotedText({ title, text })
      }
    }

    document.addEventListener("mouseup", handleSelectionChange)
    // 监听 selectionchange 事件来检测取消选择
    document.addEventListener("selectionchange", () => {
      const selection = window.getSelection()
      if (!selection?.toString().trim()) {
        setQuotedText(null)
      }
    })
    
    return () => {
      document.removeEventListener("mouseup", handleSelectionChange)
    }
  }, [isOpen, context.currentDocPath])

  // 处理图片上传
  const handleImageUpload = (e: React.ChangeEvent<HTMLInputElement>) => {
    const files = e.target.files
    if (!files) return

    Array.from(files).forEach(file => {
      // 检查文件类型
      if (!["image/png", "image/jpeg", "image/gif", "image/webp"].includes(file.type)) {
        setError({
          message: t("image.unsupportedFormat"),
          retryable: false,
        })
        return
      }

      // 检查文件大小 (10MB)
      if (file.size > 10 * 1024 * 1024) {
        setError({
          message: t("image.sizeTooLarge"),
          retryable: false,
        })
        return
      }

      const reader = new FileReader()
      reader.onload = () => {
        const base64 = reader.result as string
        setImages(prev => [...prev, base64])
      }
      reader.readAsDataURL(file)
    })

    // 清空input以便重复选择同一文件
    e.target.value = ""
  }

  // 移除图片
  const removeImage = (index: number) => {
    setImages(prev => prev.filter((_, i) => i !== index))
  }

  // 发送消息
  const handleSend = async () => {
    const trimmedInput = input.trim()
    if (!trimmedInput && images.length === 0 && !quotedText) return
    if (!selectedModelId) {
      setError({
        message: t("assistant.selectModel"),
        code: ChatErrorCodes.MODEL_UNAVAILABLE,
        retryable: false,
      })
      return
    }

    setError(null)
    setIsLoading(true)

    // 创建新的AbortController
    abortControllerRef.current = new AbortController()

    // 添加用户消息（引用文本单独存储，不合并到 content）
    const userMessageId = addMessage({
      role: "user",
      content: trimmedInput,
      images: images.length > 0 ? [...images] : undefined,
      quotedText: quotedText || undefined,
    })

    // 清空输入
    const savedInput = input
    const savedImages = [...images]
    const savedQuotedText = quotedText
    setInput("")
    setImages([])
    setQuotedText(null)

    // 准备请求
    const allMessages = [...messages, {
      id: userMessageId,
      role: "user" as const,
      content: trimmedInput,
      images: images.length > 0 ? [...images] : undefined,
      quotedText: savedQuotedText || undefined,
      timestamp: Date.now(),
    }]

    // 添加AI消息占位
    const assistantMessageId = addMessage({
      role: "assistant",
      content: "",
    })

    // 保存请求信息以便重试
    setLastRequest({
      input: savedInput,
      images: savedImages,
      userMessageId,
      assistantMessageId,
    })

    let assistantContent = ""
    let contentBlocks: ContentBlock[] = []
    let currentToolCalls: ToolCall[] = []
    // 用于跟踪当前正在构建的内容块
    let currentThinkingContent = ""

    try {
      const stream = streamChat(
        {
          messages: allMessages.map(toChatMessageDto),
          modelId: selectedModelId,
          context: {
            ...context,
            userLanguage: locale,
          },
          appId,
        },
        {
          signal: abortControllerRef.current.signal,
        }
      )

      for await (const event of stream) {
        switch (event.type) {
          case "content":
            const textContent = event.data as string
            assistantContent += textContent
            // 添加或更新 text 内容块
            const lastBlock = contentBlocks[contentBlocks.length - 1]
            if (lastBlock && lastBlock.type === "text") {
              lastBlock.content = (lastBlock.content || "") + textContent
            } else {
              contentBlocks.push({ type: "text", content: textContent })
            }
            updateMessage(assistantMessageId, { 
              content: assistantContent, 
              contentBlocks: [...contentBlocks],
              toolCalls: currentToolCalls.length > 0 ? currentToolCalls : undefined
            })
            break

          case "thinking":
            const thinkingEvent = event.data as ThinkingEvent
            if (thinkingEvent.type === "start") {
              // 开始新的 thinking 块
              currentThinkingContent = ""
              contentBlocks.push({ type: "thinking", content: "" })
            } else if (thinkingEvent.type === "delta" && thinkingEvent.content) {
              currentThinkingContent += thinkingEvent.content
              // 更新最后一个 thinking 块
              const thinkingBlock = contentBlocks.findLast(b => b.type === "thinking")
              if (thinkingBlock) {
                thinkingBlock.content = currentThinkingContent
              }
              updateMessage(assistantMessageId, { 
                content: assistantContent, 
                thinking: currentThinkingContent,
                contentBlocks: [...contentBlocks],
                toolCalls: currentToolCalls.length > 0 ? currentToolCalls : undefined
              })
            }
            break

          case "tool_call":
            const toolCallEvent = event.data as ToolCallEvent
            // 检查是否已存在相同 ID 的 tool call
            const existingIndex = currentToolCalls.findIndex(t => t.id === toolCallEvent.id)
            
            if (existingIndex >= 0) {
              // 更新已存在的 tool call（添加参数）
              if (toolCallEvent.arguments) {
                currentToolCalls[existingIndex].arguments = toolCallEvent.arguments
                // 更新对应的 contentBlock
                const blockIndex = contentBlocks.findIndex(
                  b => b.type === "tool_call" && b.toolCall?.id === toolCallEvent.id
                )
                if (blockIndex >= 0) {
                  contentBlocks[blockIndex].toolCall = currentToolCalls[existingIndex]
                }
              }
            } else {
              // 新的 tool call
              const newToolCall: ToolCall = {
                id: toolCallEvent.id,
                name: toolCallEvent.name,
                arguments: toolCallEvent.arguments || {}
              }
              currentToolCalls = [...currentToolCalls, newToolCall]
              contentBlocks.push({ type: "tool_call", toolCall: newToolCall })
            }
            
            updateMessage(assistantMessageId, {
              content: assistantContent,
              contentBlocks: [...contentBlocks],
              toolCalls: [...currentToolCalls],
            })
            break

          case "tool_result":
            const toolResult = event.data as ToolResult
            // 添加工具结果消息
            addMessage({
              role: "tool",
              content: toolResult.result,
              toolResult,
            })
            break

          case "done":
            // 对话完成，清除重试信息
            setLastRequest(null)
            break

          case "error":
            const errorInfo = event.data as ErrorInfo
            setError({
              message: errorInfo.message || getErrorMessage(errorInfo.code),
              code: errorInfo.code,
              retryable: errorInfo.retryable ?? isRetryableError(errorInfo.code),
              retryAfterMs: errorInfo.retryAfterMs,
            })
            break
        }
      }
    } catch (err) {
      console.error(t("error.chatFailed"), err)
      setError({
        message: err instanceof Error ? err.message : t("error.chatFailed"),
        retryable: true,
      })
    } finally {
      setIsLoading(false)
      abortControllerRef.current = null
    }
  }

  // 重试发送
  const handleRetry = async () => {
    if (!lastRequest) return
    
    // 恢复输入状态
    setInput(lastRequest.input)
    setImages(lastRequest.images)
    setError(null)
    
    // 重新发送
    handleSend()
  }

  // 处理键盘事件
  const handleKeyDown = (e: React.KeyboardEvent<HTMLTextAreaElement>) => {
    if (e.key === "Enter" && !e.shiftKey) {
      e.preventDefault()
      handleSend()
    }
  }

  if (!isOpen) return null

  const enabledModels = models.filter(m => m.isEnabled)
  const canSend = (input.trim() || images.length > 0) && selectedModelId && !isLoading

  return (
    <>
      {/* 背景遮罩 - 不关闭面板，仅作为视觉分隔 */}
      <div
        className="fixed inset-0 z-40 bg-black/20 pointer-events-none"
      />

      {/* 对话面板 */}
      <div
        ref={panelRef}
        style={{ width: panelWidth }}
        className={cn(
          "fixed right-0 top-0 z-50 flex h-full flex-col",
          "bg-background shadow-xl",
          "transform transition-transform duration-300 ease-in-out",
          isOpen ? "translate-x-0" : "translate-x-full"
        )}
      >
        {/* 左侧拖动条 */}
        <div
          onMouseDown={handleResizeStart}
          className="absolute left-0 top-0 bottom-0 w-1 cursor-col-resize hover:bg-primary/20 active:bg-primary/30 transition-colors group flex items-center"
        >
          <div className="absolute left-0 w-4 h-full" />
          <GripVertical className="h-6 w-6 text-muted-foreground/30 group-hover:text-muted-foreground/60 -ml-2.5" />
        </div>

        {/* 头部 */}
        <div className="flex items-center justify-between px-4 py-3">
          <h2 className="font-semibold">{t("assistant.title")}</h2>
          <div className="flex items-center gap-1">
            <Button
              variant="ghost"
              size="icon"
              onClick={handleOpenShareDialog}
              title={t("panel.shareConversation")}
              disabled={messages.length === 0}
            >
              <Share2 className="h-4 w-4" />
            </Button>
            <Button
              variant="ghost"
              size="icon"
              onClick={clearHistory}
              title={t("panel.clearHistory")}
              disabled={messages.length === 0}
            >
              <Trash2 className="h-4 w-4" />
            </Button>
            <Button variant="ghost" size="icon" onClick={onClose}>
              <X className="h-4 w-4" />
            </Button>
          </div>
        </div>

        {/* 消息列表 - 底部留出空间给悬浮输入框 */}
        <ScrollArea className="flex-1 overflow-hidden w-full">
          <div className="flex flex-col w-full">
            {!isEnabled ? (
              <div className="flex h-full items-center justify-center p-8 text-center text-muted-foreground">
                {t("assistant.disabled")}
              </div>
            ) : enabledModels.length === 0 ? (
              <div className="flex h-full items-center justify-center p-8 text-center text-muted-foreground">
                {t("assistant.noModels")}
              </div>
            ) : messages.length === 0 ? (
              <div className="flex h-full items-center justify-center p-8 text-center text-muted-foreground">
                <div>
                  <p className="mb-2">{t("assistant.greeting")}</p>
                  <p className="text-sm">{t("assistant.greetingSubtitle")}</p>
                </div>
              </div>
            ) : (
              <div className="w-full">
                {messages.map((message, index) => (
                  <div
                    key={message.id}
                    className="animate-in fade-in-0 slide-in-from-bottom-2 duration-300"
                    style={{ animationDelay: `${Math.min(index * 50, 200)}ms` }}
                  >
                    <ChatMessageItem message={message} />
                  </div>
                ))}
              </div>
            )}

            {/* 加载指示器 */}
            {isLoading && (
              <div className="flex items-center gap-2 p-4 text-muted-foreground animate-in fade-in-0 duration-200">
                <div className="flex gap-1">
                  <span className="w-2 h-2 bg-primary/60 rounded-full animate-bounce" style={{ animationDelay: '0ms' }} />
                  <span className="w-2 h-2 bg-primary/60 rounded-full animate-bounce" style={{ animationDelay: '150ms' }} />
                  <span className="w-2 h-2 bg-primary/60 rounded-full animate-bounce" style={{ animationDelay: '300ms' }} />
                </div>
                <span className="text-sm">{t("assistant.thinking")}</span>
              </div>
            )}
            
            {/* 滚动锚点 + 底部空白区域，确保内容不被输入框遮挡 */}
            <div ref={messagesEndRef} className="h-52 shrink-0" />
          </div>
        </ScrollArea>

        {/* 错误提示 */}
        {error && (
          <div className="absolute bottom-44 left-4 right-4 rounded-lg border border-destructive/50 bg-destructive/10 px-4 py-2 text-sm text-destructive shadow-lg">
            <div className="flex items-center justify-between">
              <span>{error.message}</span>
              <div className="flex items-center gap-2">
                {error.retryable && lastRequest && (
                  <button
                    className="flex items-center gap-1 underline hover:no-underline"
                    onClick={handleRetry}
                    disabled={isLoading}
                  >
                    <RefreshCw className="h-3 w-3" />
                    {t("panel.retry")}
                  </button>
                )}
                <button
                  className="underline hover:no-underline"
                  onClick={() => setError(null)}
                >
                  {t("panel.closeError")}
                </button>
              </div>
            </div>
          </div>
        )}

        {/* 悬浮输入区域 - ChatGPT 风格 */}
        <div className="absolute bottom-0 left-0 right-0 p-3">
          <div className="rounded-2xl border border-border/50 bg-background/90 backdrop-blur-sm shadow-lg">
            {/* 引用文本预览 */}
            {quotedText && (
              <div className="border-b border-border/50 px-3 py-2">
                <div className="flex items-start justify-between gap-2">
                  <div className="flex-1 overflow-hidden">
                    <div className="text-xs text-primary font-medium mb-1 flex items-center gap-1">
                      <span>{t("quote.icon")}</span>
                      <span>{t("quote.label")}{quotedText.title || t("message.currentPage")}</span>
                    </div>
                    <pre className="text-xs text-muted-foreground whitespace-pre-wrap break-words max-h-16 overflow-y-auto">
                      {quotedText.text}
                    </pre>
                  </div>
                  <button
                    type="button"
                    onClick={() => setQuotedText(null)}
                    className="shrink-0 rounded p-1 hover:bg-muted"
                  >
                    <X className="h-3 w-3 text-muted-foreground" />
                  </button>
                </div>
              </div>
            )}

            {/* 图片预览 */}
            {images.length > 0 && (
              <div className="border-b border-border/50 px-3 py-2">
                <div className="flex flex-wrap gap-2">
                  {images.map((img, index) => (
                    <div key={index} className="relative">
                      <img
                        src={img}
                        alt={t("image.preview", { index: index + 1 })}
                        className="h-10 w-10 rounded-lg object-cover border"
                      />
                      <button
                        type="button"
                        onClick={() => removeImage(index)}
                        className="absolute -right-1 -top-1 rounded-full bg-destructive p-0.5 text-destructive-foreground shadow"
                      >
                        <X className="h-3 w-3" />
                      </button>
                    </div>
                  ))}
                </div>
              </div>
            )}

            {/* 输入框 */}
            <div className="px-3 py-1.5">
              <Textarea
                ref={inputRef}
                value={input}
                onChange={(e) => setInput(e.target.value)}
                onKeyDown={handleKeyDown}
                placeholder={t("panel.inputPlaceholder")}
                className="min-h-[100px] resize-none border-0 !bg-transparent p-0 text-sm leading-5 focus-visible:ring-0 focus-visible:ring-offset-0 placeholder:text-muted-foreground/60 shadow-none"
                disabled={!isEnabled || enabledModels.length === 0 || isLoading}
                rows={5}
              />
            </div>

            {/* 底部工具栏 */}
            <div className="flex items-center justify-between border-t border-border/50 px-2 py-1">
              {/* 左侧按钮 */}
              <div className="flex items-center gap-1">
                <input
                  ref={fileInputRef}
                  type="file"
                  accept="image/png,image/jpeg,image/gif,image/webp"
                  multiple
                  className="hidden"
                  onChange={handleImageUpload}
                />
                {enableImageUpload && (
                  <Button
                    variant="ghost"
                    size="icon"
                    className="h-6 w-6 rounded-md"
                    onClick={() => fileInputRef.current?.click()}
                    disabled={!isEnabled || enabledModels.length === 0}
                    title={t("panel.uploadImage")}
                  >
                    <ImagePlus className="h-3.5 w-3.5" />
                  </Button>
                )}
              </div>

              {/* 右侧：模型选择 + 发送按钮 */}
              <div className="flex items-center gap-1.5">
                <ModelSelector
                  models={models}
                  selectedModelId={selectedModelId}
                  onModelChange={setSelectedModelId}
                  disabled={isLoading}
                />
                <Button
                  onClick={handleSend}
                  disabled={!canSend}
                  size="icon"
                  className={cn(
                    "h-6 w-6 rounded-md transition-all",
                    canSend ? "bg-primary hover:bg-primary/90" : "bg-muted text-muted-foreground"
                  )}
                >
                  {isLoading ? (
                    <Loader2 className="h-3.5 w-3.5 animate-spin" />
                  ) : (
                    <Send className="h-3.5 w-3.5" />
                  )}
                </Button>
              </div>
            </div>
          </div>
        </div>
      </div>
      <Dialog open={isShareDialogOpen} onOpenChange={handleShareDialogChange}>
        <DialogContent className="sm:max-w-[520px]">
          <DialogHeader>
            <DialogTitle>{t("panel.shareDialogTitle")}</DialogTitle>
            <DialogDescription>
              {t("panel.shareDialogDescription")}
            </DialogDescription>
          </DialogHeader>

          {shareResult ? (
            <div className="space-y-4">
              <div className="rounded-xl border border-border bg-muted/40 px-4 py-3 text-sm">
                <div className="text-muted-foreground">{t("panel.shareLink")}</div>
                <p className="mt-1 break-all text-foreground">{shareLink}</p>
                <p className="mt-2 text-xs text-muted-foreground">
                  {t("panel.shareId")}: {shareResult.shareId} · Model: {shareResult.modelId}
                </p>
              </div>
              <div className="flex flex-wrap gap-2">
                <Button onClick={handleCopyShareLink} className="gap-2">
                  {shareCopied ? (
                    <>
                      <Check className="h-4 w-4" /> {t("panel.copied")}
                    </>
                  ) : (
                    <>
                      <Copy className="h-4 w-4" /> {t("panel.copyLink")}
                    </>
                  )}
                </Button>
                <Button
                  variant="secondary"
                  onClick={() => window.open(shareLink, "_blank", "noopener,noreferrer")}
                  disabled={!shareLink}
                >
                  {t("panel.openSharePage")}
                </Button>
                <Button variant="ghost" onClick={() => handleShareDialogChange(false)}>
                  {t("panel.close")}
                </Button>
              </div>
            </div>
          ) : (
            <>
              <div className="space-y-4">
                <div className="space-y-2">
                  <label className="text-sm font-medium">{t("panel.shareTitle")}</label>
                  <Input
                    value={shareTitle}
                    placeholder={t("panel.shareTitlePlaceholder")}
                    onChange={(e) => setShareTitle(e.target.value)}
                  />
                </div>
                <div className="space-y-2">
                  <label className="text-sm font-medium">{t("panel.shareDescription")}</label>
                  <Textarea
                    value={shareDescription}
                    rows={3}
                    placeholder={t("panel.shareDescriptionPlaceholder")}
                    onChange={(e) => setShareDescription(e.target.value)}
                  />
                </div>
                <div className="space-y-2">
                  <label className="text-sm font-medium">{t("panel.shareExpiration")}</label>
                  <Input
                    type="number"
                    min={10}
                    max={60 * 24 * 30}
                    value={shareExpireMinutes}
                    onChange={(e) => {
                      const value = parseInt(e.target.value, 10)
                      if (!Number.isNaN(value)) {
                        setShareExpireMinutes(Math.min(Math.max(value, 10), 60 * 24 * 30))
                      }
                    }}
                  />
                  <p className="text-xs text-muted-foreground">{t("panel.shareExpirationHint")}</p>
                </div>
                {shareError && (
                  <p className="text-sm text-destructive">{shareError}</p>
                )}
              </div>
              <DialogFooter>
                <Button variant="outline" onClick={() => handleShareDialogChange(false)}>
                  {common("cancel")}
                </Button>
                <Button onClick={handleCreateShare} disabled={shareLoading}>
                  {shareLoading ? (
                    <Loader2 className="mr-2 h-4 w-4 animate-spin" />
                  ) : null}
                  {shareLoading ? t("panel.generating") : t("panel.generateShareLink")}
                </Button>
              </DialogFooter>
            </>
          )}
        </DialogContent>
      </Dialog>
    </>
  )
}
