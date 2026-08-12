// ADR-083 -- "the recording agent is the capturing authority, not the
// device's raw sensor and not the server." One instance per capture
// session; every adapter that wants MonotonicElapsedMicros populated
// creates one at session start and asks it for each reading's elapsed
// time. `performance.now()` is a monotonic clock source immune to
// wall-clock adjustment/NTP correction -- exactly what this ADR asks
// for -- available in every browser this client targets (no feature
// detection needed, unlike the Web Hardware APIs below it).
export class RecordingAgent {
  private readonly startedAtMonotonic: number

  constructor() {
    this.startedAtMonotonic = performance.now()
  }

  // Microseconds elapsed since this agent's own construction -- not
  // since the device's power-on or any other clock, per ADR-083's own
  // "session start" framing.
  elapsedMicros(): number {
    return Math.round((performance.now() - this.startedAtMonotonic) * 1000)
  }
}
