const parser = require('@typescript-eslint/parser')
const typescript = require('@typescript-eslint/eslint-plugin')
const hooks = require('eslint-plugin-react-hooks')
const refresh = require('eslint-plugin-react-refresh').default
module.exports = [
  { ignores: ['dist/**', 'node_modules/**', 'release/**'] },
  {
    files: ['src/**/*.ts', 'src/**/*.tsx'],
    languageOptions: { parser, ecmaVersion: 'latest', sourceType: 'module' },
    plugins: { '@typescript-eslint': typescript, 'react-hooks': hooks, 'react-refresh': refresh },
    rules: {
      ...typescript.configs.recommended.rules,
      'react-hooks/rules-of-hooks': 'error',
      'react-hooks/exhaustive-deps': 'warn',
      'react-refresh/only-export-components': ['warn', { allowConstantExport: true }],
      '@typescript-eslint/no-unused-vars': ['error', { argsIgnorePattern: '^_' }],
      '@typescript-eslint/no-explicit-any': 'warn',
    },
  },
]
