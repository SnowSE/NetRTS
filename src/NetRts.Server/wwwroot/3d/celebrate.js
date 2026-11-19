// 3D match view: when a match is won, the winner's badgers celebrate: each hops up and down and pumps both arms
// overhead, on its own beat so the crowd doesn't move in lockstep. Snow Mobiles just bounce.

/** The winner's slot once the match is over with a winner, else null. */
export function winnerSlot(state) {
  const id = state?.outcome?.winnerId;
  if (!id) return null;
  return state.players?.find((p) => p.playerId === id)?.slot ?? null;
}

/** One dancer at time now (ms): hop 0..1 (off the ground) and arms 0..1 (raised overhead). */
export function dance(now, id) {
  const t = now * 0.009 + id * 1.7;
  return { hop: Math.abs(Math.sin(t)), arms: 0.5 + 0.5 * Math.sin(t * 2 - 0.6) };
}
