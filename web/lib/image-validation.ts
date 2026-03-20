/**
 * 图片验证工具函数
 * 支持国际化的错误消息
 */

export const SUPPORTED_IMAGE_TYPES = [
  "image/png",
  "image/jpeg",
  "image/gif",
  "image/webp",
] as const

export const MAX_IMAGE_SIZE = 10 * 1024 * 1024

/**
 * 验证图片文件
 * @param file 文件对象
 * @param t 翻译函数
 */
export function validateImageFile(
  file: File,
  t?: (key: string, values?: Record<string, any>) => string
): { valid: boolean; error?: string } {
  // 检查文件类型
  if (!SUPPORTED_IMAGE_TYPES.includes(file.type as typeof SUPPORTED_IMAGE_TYPES[number])) {
    return {
      valid: false,
      error: t
        ? t("chat.image.unsupportedFormat")
        : `Unsupported image format: ${file.type}. Supported formats: PNG, JPG, GIF, WebP`,
    }
  }

  // 检查文件大小
  if (file.size > MAX_IMAGE_SIZE) {
    const sizeMB = (file.size / (1024 * 1024)).toFixed(2)
    return {
      valid: false,
      error: t
        ? t("chat.image.sizeTooLarge")
        : `Image size (${sizeMB}MB) exceeds the 10MB limit`,
    }
  }

  return { valid: true }
}

/**
 * 将文件转换为Base64
 */
export function fileToBase64(file: File): Promise<string> {
  return new Promise((resolve, reject) => {
    const reader = new FileReader()
    reader.onload = () => resolve(reader.result as string)
    reader.onerror = () => reject(new Error("Failed to read file"))
    reader.readAsDataURL(file)
  })
}

/**
 * 从Base64字符串中提取MIME类型
 */
export function getMimeTypeFromBase64(base64: string): string | null {
  const match = base64.match(/^data:([^;]+);base64,/)
  return match ? match[1] : null
}

/**
 * 验证Base64图片格式
 * @param base64 Base64字符串
 * @param t 翻译函数
 */
export function validateBase64Image(
  base64: string,
  t?: (key: string) => string
): { valid: boolean; error?: string } {
  const mimeType = getMimeTypeFromBase64(base64)

  if (!mimeType) {
    return {
      valid: false,
      error: t ? t("chat.image.invalidFormat") : "Invalid Base64 image format",
    }
  }

  if (!SUPPORTED_IMAGE_TYPES.includes(mimeType as typeof SUPPORTED_IMAGE_TYPES[number])) {
    return {
      valid: false,
      error: t
        ? t("chat.image.unsupportedFormat")
        : `Unsupported image format: ${mimeType}. Supported formats: PNG, JPG, GIF, WebP`,
    }
  }

  return { valid: true }
}
