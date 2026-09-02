// @ts-check
//
// ESLint for the FTMS Angular client.
//
// Prettier already runs in CI, but formatting is not linting: `prettier --check` cannot see an
// unawaited promise, a subscription that is never closed, or a template that is unreachable by
// keyboard. This config covers the correctness half. Formatting stays entirely Prettier's job -
// no stylistic rule below overlaps with it.
//
// Scope note: this is the client's own toolchain. The .NET side is governed by the repo root
// .editorconfig, which stops at clients/ because this project declares `root = true`.

const eslint = require('@eslint/js');
const tseslint = require('typescript-eslint');
const angular = require('angular-eslint');

module.exports = tseslint.config(
  {
    // The generated API layer is machine output, regenerated from docs/api/openapi-v1.json and
    // verified byte for byte in CI. A lint rule that wants a change here cannot win: the
    // generator overwrites it on the next run and the CI drift check fails either way.
    // Generated code is held to its generator, never to a style rule.
    ignores: [
      'src/app/core/api/generated/**',
      'dist/**',
      '.angular/**',
      'coverage/**',
      'playwright-report/**',
      'test-results/**',
    ],
  },

  // -------------------------------------------------------------------------------------------
  // TypeScript
  // -------------------------------------------------------------------------------------------
  {
    files: ['**/*.ts'],
    extends: [
      eslint.configs.recommended,
      ...tseslint.configs.recommended,
      ...tseslint.configs.stylistic,
      ...angular.configs.tsRecommended,
    ],
    // Lets the template rules below reach templates written inline in a @Component.
    processor: angular.processInlineTemplates,
    rules: {
      // The `ftms-` prefix keeps our elements distinguishable from Angular CDK and from any
      // future second client sharing a page.
      '@angular-eslint/directive-selector': [
        'error',
        { type: 'attribute', prefix: 'ftms', style: 'camelCase' },
      ],
      '@angular-eslint/component-selector': [
        'error',
        { type: 'element', prefix: 'ftms', style: 'kebab-case' },
      ],

      // An unused variable is either a mistake or a leftover. Underscore is the escape hatch
      // for the genuinely deliberate case, such as an ignored destructured field.
      '@typescript-eslint/no-unused-vars': [
        'error',
        {
          argsIgnorePattern: '^_',
          varsIgnorePattern: '^_',
          caughtErrorsIgnorePattern: '^_',
        },
      ],

      // `any` defeats the reason the API client is generated from the contract at all.
      '@typescript-eslint/no-explicit-any': 'error',
    },
  },

  // -------------------------------------------------------------------------------------------
  // Templates
  //
  // Accessibility rules are on deliberately. FTMS is a system of record that people operate all
  // day; keyboard reachability and label association are operability, not decoration.
  // -------------------------------------------------------------------------------------------
  {
    files: ['**/*.html'],
    extends: [...angular.configs.templateRecommended, ...angular.configs.templateAccessibility],
    rules: {},
  },

  // -------------------------------------------------------------------------------------------
  // Vendored ZardUI source
  //
  // Copied in by `zard-cli add`, not written here. We own these files and edit them when our
  // compiler demands it, but the `z-` selector prefix is the library's public API: renaming it
  // would break the templates and every future `zard-cli add` would reintroduce the conflict.
  // Only the prefix rules are relaxed - correctness rules still apply to this code.
  // -------------------------------------------------------------------------------------------
  {
    files: ['src/app/shared/components/**/*.ts', 'src/app/shared/core/**/*.ts'],
    rules: {
      '@angular-eslint/component-selector': 'off',
      '@angular-eslint/directive-selector': 'off',
    },
  },

  // -------------------------------------------------------------------------------------------
  // Tests and Playwright journeys
  // -------------------------------------------------------------------------------------------
  {
    files: ['**/*.spec.ts', 'e2e/**/*.ts'],
    rules: {
      // Test doubles and fixture builders legitimately reach for `any` where reproducing a full
      // generated DTO would obscure what the test is actually asserting.
      '@typescript-eslint/no-explicit-any': 'off',

      // `onConfirm: async () => {}` is the point of the assertion, not an oversight: the test is
      // checking what the dialog renders, and supplying a callback that does nothing is the
      // clearest way to say the callback is irrelevant here.
      '@typescript-eslint/no-empty-function': 'off',
    },
  },
);
