import { readFile, writeFile, readdir, stat } from 'fs/promises'
import { join, resolve, relative } from 'path'
import type { ToolDefinition, ExecutionContext, ToolResult } from '../types'
import { z } from 'zod'

const ReadFileSchema = z.object({
  path: z.string(),
})

const WriteFileSchema = z.object({
  path: z.string(),
  content: z.string(),
})

const ListFilesSchema = z.object({
  path: z.string(),
  recursive: z.boolean().optional(),
})

export const readFileTool: ToolDefinition = {
  name: 'read_file',
  description: 'Read the contents of a text file',
  inputSchema: ReadFileSchema,
  capabilities: {
    requiresPermission: 'read',
    canCancel: false,
    longRunning: false,
  },

  async execute(input: unknown, context: ExecutionContext): Promise<ToolResult> {
    try {
      const { path } = ReadFileSchema.parse(input)
      const absolutePath = resolve(path)

      // Check if path is within allowed project directories
      if (context.permissions === 'project') {
        const isAllowed = context.projectDirs.some((dir) => {
          const relPath = relative(dir, absolutePath)
          return !relPath.startsWith('..') && !relPath.startsWith('/')
        })

        if (!isAllowed) {
          return {
            success: false,
            error: `Path ${path} is outside project directories`,
          }
        }
      }

      const content = await readFile(absolutePath, 'utf-8')

      return {
        success: true,
        output: content,
      }
    } catch (error) {
      return {
        success: false,
        error: error instanceof Error ? error.message : 'Failed to read file',
      }
    }
  },
}

export const writeFileTool: ToolDefinition = {
  name: 'write_file',
  description: 'Write content to a file (creates or overwrites)',
  inputSchema: WriteFileSchema,
  capabilities: {
    requiresPermission: 'write',
    canCancel: false,
    longRunning: false,
  },

  async execute(input: unknown, context: ExecutionContext): Promise<ToolResult> {
    try {
      const { path, content } = WriteFileSchema.parse(input)
      const absolutePath = resolve(path)

      // Check permissions
      if (context.permissions === 'project') {
        const isAllowed = context.projectDirs.some((dir) => {
          const relPath = relative(dir, absolutePath)
          return !relPath.startsWith('..') && !relPath.startsWith('/')
        })

        if (!isAllowed) {
          return {
            success: false,
            error: `Path ${path} is outside project directories`,
          }
        }
      }

      // Request approval for write operation
      if (context.permissions === 'ask') {
        const approval = await context.requestApproval({
          id: `write_${Date.now()}`,
          action: 'write_file',
          target: absolutePath,
          details: { contentLength: content.length },
        })

        if (!approval.approved) {
          return {
            success: false,
            error: 'Write operation denied by user',
          }
        }
      }

      await writeFile(absolutePath, content, 'utf-8')

      return {
        success: true,
        output: `File written successfully: ${path}`,
      }
    } catch (error) {
      return {
        success: false,
        error: error instanceof Error ? error.message : 'Failed to write file',
      }
    }
  },
}

export const listFilesTool: ToolDefinition = {
  name: 'list_files',
  description: 'List files and directories in a path',
  inputSchema: ListFilesSchema,
  capabilities: {
    requiresPermission: 'read',
    canCancel: false,
    longRunning: false,
  },

  async execute(input: unknown, context: ExecutionContext): Promise<ToolResult> {
    try {
      const { path, recursive = false } = ListFilesSchema.parse(input)
      const absolutePath = resolve(path)

      // Check permissions
      if (context.permissions === 'project') {
        const isAllowed = context.projectDirs.some((dir) => {
          const relPath = relative(dir, absolutePath)
          return !relPath.startsWith('..') && !relPath.startsWith('/')
        })

        if (!isAllowed) {
          return {
            success: false,
            error: `Path ${path} is outside project directories`,
          }
        }
      }

      const files = await listDirectory(absolutePath, recursive)

      return {
        success: true,
        output: files.join('\n'),
      }
    } catch (error) {
      return {
        success: false,
        error: error instanceof Error ? error.message : 'Failed to list files',
      }
    }
  },
}

async function listDirectory(dirPath: string, recursive: boolean): Promise<string[]> {
  const results: string[] = []
  const entries = await readdir(dirPath, { withFileTypes: true })

  for (const entry of entries) {
    const fullPath = join(dirPath, entry.name)
    const stats = await stat(fullPath)

    if (entry.isDirectory()) {
      results.push(`${fullPath}/ (directory)`)
      if (recursive) {
        const subFiles = await listDirectory(fullPath, true)
        results.push(...subFiles)
      }
    } else {
      results.push(`${fullPath} (${stats.size} bytes)`)
    }
  }

  return results
}

export const FILE_TOOLS = [readFileTool, writeFileTool, listFilesTool]
