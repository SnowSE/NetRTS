// What the Formics "say" in the 3D view. The hive doesn't speak aloud; it thinks as one, so instead of a
// recorded voice its moments come through as a chitter of mandibles (audio.js) and a caption of the hive's
// thought. Same moments as the players' lines (tools/voices/lines.json), same pacing (commentary.js).

export const HIVE_LINES = {
  start: ['The hive wakes.', 'We are many. We begin.', 'Spread out. Find the food.', 'A new nest. Dig.', 'We wake together.'],
  gather: ['Gather. Carry. Return.', 'Every grub feeds the hive.', 'The workers do not tire.', 'More food. Always more.'],
  build: ['The nest grows.', 'Raise another chamber.', 'Dig here.', 'Shape the earth.'],
  built: ['The chamber is ready.', 'The nest is stronger.', 'It is finished. Build again.'],
  built_CommandCenter: ['A new nest. The hive spreads.', 'A second heart for the hive.'],
  built_Barracks: ['The brood chamber opens.', 'Now we hatch warriors.'],
  built_ResourceDepot: ['A storehouse. Shorter paths.', 'Bring the food here now.'],
  built_TechLab: ['Now the hive learns.', 'A chamber for thinking.'],
  built_GuardTower: ['A sentinel stands watch.', 'Let them come to the spire.'],
  train: ['Another one hatches.', 'The brood grows.'],
  train_Worker: ['A new worker. It knows its task.', 'Another pair of claws.'],
  train_Soldier: ['A warrior hatches.', 'Sharpen the scythes.'],
  train_Archer: ['A spitter, full of acid.', 'Fill the sacs.'],
  train_Scout: ['Wings. Go and see for us.', 'Fly. We will see what you see.'],
  attack: ['Now. All of us.', 'Swarm.', 'Overwhelm them.', 'We move as one.', 'Their gate is open. Go.'],
  kill: ['One less.', 'It fell.', 'They break so easily.', 'Too slow.'],
  lost_unit: ['One of us is gone. We are still many.', 'We felt that.', 'A drone is lost. Hatch another.'],
  under_attack: ['They are in the nest!', 'Defend the queen.', 'Intruders. All warriors home.', 'The nest is bleeding.'],
  destroyed_building: ['Their nest crumbles.', 'Tear it down.', 'Nothing will grow there again.'],
  lost_building: ['A chamber is lost.', 'They broke the nest. Rebuild.', 'We will remember this.'],
  tower: ['The sentinel stings.', 'The spire sees them.'],
  research: ['The hive has learned.', 'We adapt.', 'Harder shells. Sharper claws.'],
  ahead: ['The hive grows. They shrink.', 'We are winning. We always win.', 'Soon there will be only us.'],
  behind: ['They are many too. Interesting.', 'Hatch faster.', 'We must grow.'],
  taunt: ['You fight alone. We never do.', 'You think. We know.', 'Your workers are slow.', 'We can hear your plans.',
    'How small your army is.', 'We do not sleep.', 'You are one. We are all.'],
  chatter: ['The queen is patient.', 'The tunnels are deep.', 'We are listening.', 'Hungry. Always hungry.'],
  eliminated: ['The queen... is silent.', 'The hive... goes quiet.', 'We are... scattered.'],
  victory: ['The hive endures.', 'Only the hive remains.', 'This world is ours.'],
  defeat: ['The hive will return.', 'We will remember you.', 'Another hive will hatch.'],
};

/** How long a thought stays up: long enough to read. */
export const readingSecs = (text) => Math.max(1.8, text.split(' ').length * 0.32);
