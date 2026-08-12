import { createDpopProof } from './dpop'

// ADR-031/070 -- irregular/event-driven ingest shape only (a device
// reading has no fixed sample rate the way a periodic sensor would);
// mirrors EventStore.Streaming.IngestSamplesRequest/IrregularSampleRequest
// exactly. The fixed-rate (StartTimestamp/SampleIntervalMicros/Values)
// shape has no client-web producer -- nothing in this codebase captures a
// truly fixed-rate stream client-side.
export interface IrregularSample {
  timestamp: string
  value: unknown
  monotonicElapsedMicros?: number
}

export interface IngestSamplesResult {
  ok: boolean
  samplesWritten?: number
  lateArrivalCount?: number
}

export async function ingestSamples(hostBaseUrl: string, token: string, channelId: string, samples: IrregularSample[]): Promise<IngestSamplesResult> {
  const url = `${hostBaseUrl}/telemetry/${channelId}/samples`
  const response = await fetch(url, {
    method: 'POST',
    headers: {
      'Content-Type': 'application/json',
      Authorization: `Bearer ${token}`,
      DPoP: await createDpopProof('POST', url, token), // ADR-017
    },
    body: JSON.stringify({
      samples: samples.map((s) => ({ timestamp: s.timestamp, value: s.value, monotonicElapsedMicros: s.monotonicElapsedMicros })),
    }),
  })
  if (!response.ok) return { ok: false }
  const body = (await response.json()) as { samplesWritten: number; lateArrivalCount: number }
  return { ok: true, samplesWritten: body.samplesWritten, lateArrivalCount: body.lateArrivalCount }
}
