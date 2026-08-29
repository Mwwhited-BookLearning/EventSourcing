import { fetchToken } from '../api/authClient'
import { exportLineage, downloadBundle } from '../api/playbackClient'
import type { FetchTokenFn } from './useEntityViewActions'

// ADR-068 "Lineage Export & Bitemporal Playback" -- Vitals' own Workflow C
// (Trial Data Export and Subject Rights) had no client-web UI surface for
// this at all: exportLineage/downloadBundle (playbackClient.ts) and
// BitemporalPlaybackControl.vue/OfflineBundleViewer.vue all already
// existed, built and unit-tested, but nothing in App.vue ever wired them
// together into a reachable screen (confirmed by grep -- neither
// component is imported anywhere outside its own spec file). This
// composable is the missing glue: a single entityId feeds the real
// GraphQL exportLineage query, then downloads the resulting bundle from
// its own bundleUrl, same two-step handoff LineageExportQueries.cs's own
// comment documents (a produced artifact, never held server-side beyond
// its 15-minute retrieval window).
export interface LineageExportConfig {
  hostBaseUrl: string
  authBaseUrl: string
}

export interface LineageExportResult {
  ok: boolean
  bundleNdjson?: string
  error?: string
}

export function useLineageExportAndPlayback(config: LineageExportConfig, deps: { fetchToken?: FetchTokenFn } = {}) {
  const tokenFetcher = deps.fetchToken ?? fetchToken
  let token: string | null = null

  // follower-client already holds events:lineage:read (DevIdpSeeder.cs) --
  // the same read-only identity useEntityViewActions.ts's own generic
  // Detail/Browse subscription uses, not a new one invented for this panel.
  async function ensureToken(): Promise<string> {
    token ??= await tokenFetcher(config.authBaseUrl, 'follower-client', 'follower-client-secret', 'events:follow events:lineage:read')
    return token
  }

  async function exportBundle(entityId: string): Promise<LineageExportResult> {
    try {
      const currentToken = await ensureToken()
      const bundleUrl = await exportLineage(config.hostBaseUrl, currentToken, entityId)
      const bundleNdjson = await downloadBundle(config.hostBaseUrl, currentToken, bundleUrl)
      return { ok: true, bundleNdjson }
    } catch (error) {
      return { ok: false, error: (error as Error).message }
    }
  }

  async function getToken(): Promise<string> {
    return ensureToken()
  }

  return { exportBundle, getToken }
}
