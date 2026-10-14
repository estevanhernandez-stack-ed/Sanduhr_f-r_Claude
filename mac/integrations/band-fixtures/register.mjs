// `node --import ./register.mjs generate.ts`: Node runs the mod's TypeScript as it is (type
// stripping) but needs the `.ts` its imports leave off. This hook adds it to a relative import
// that doesn't resolve as written.
import { registerHooks } from 'node:module'

registerHooks({
  resolve(specifier, context, next) {
    try {
      return next(specifier, context)
    } catch (error) {
      if (specifier.startsWith('.') && !specifier.endsWith('.ts')) {
        return next(`${specifier}.ts`, context)
      }

      throw error
    }
  },
})
