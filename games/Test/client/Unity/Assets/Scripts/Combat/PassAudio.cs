using System;
using Test.Application.Combat;
using Test.Domain.Combat;
using UnityEngine;

namespace Test.Client.Combat
{
    internal sealed class PassAudio
    {
        private readonly AudioSource theme, percussion, ambient, foley, result;
        private readonly AudioClip victory, defeat;
        private readonly AudioClip[][] clips;
        private readonly int[] variants;
        private readonly AttackPhase[] phases = new AttackPhase[4];
        private readonly int[] segments = new int[4];
        private readonly float musicVolume, ambientVolume;
        private readonly float effectsVolume;
        private float musicGain = 1, effectsGain = 1;
        private RunStatus lastStatus = RunStatus.Fighting;
        private int nextFootstep, previousArrows;
        private bool dodged, loopsStarted;
        private float tension;

        internal PassAudio(PassWorld world, float volume, float musicLevel)
        {
            volume = Mathf.Clamp01(volume);
            musicVolume = volume * Mathf.Clamp01(musicLevel);
            ambientVolume = volume * .63f;
            effectsVolume = volume;
            var audio = PassWorld.Group("Procedural audio", world.Root.transform);
            theme = Source(audio, "Music theme", 0, true);
            percussion = Source(audio, "Combat percussion", 0, true);
            ambient = Source(audio, "Environment loop", 0, true);
            foley = Source(audio, "Combat sounds", volume, false);
            result = Source(audio, "Result cadence", musicVolume, false);
            if (volume == 0) return;
            if (musicVolume > 0)
            {
                (theme.clip, percussion.clip) = PassScore.Render(world);
                victory = PassScore.Stinger(world, true);
                defeat = PassScore.Stinger(world, false);
            }
            ambient.clip = PassSound.Ambient(world);
            int soundCount = Enum.GetValues(typeof(PassSoundId)).Length;
            clips = new AudioClip[soundCount][];
            variants = new int[soundCount];
            for (int index = 0; index < soundCount; index++)
            {
                var sound = (PassSoundId)index;
                int count = sound == PassSoundId.Gate || sound == PassSoundId.Finish ? 1 : 3;
                clips[index] = new AudioClip[count];
                for (int variant = 0; variant < count; variant++) clips[index][variant] = PassSound.Foley(world, sound, variant);
            }
        }

        internal (float Music, float Effects) Mix => (theme.volume, foley.volume);

        internal void SetMix(float music, float effects)
        {
            musicGain = music;
            effectsGain = effects;
            foley.volume = effectsVolume * effectsGain;
            result.volume = musicVolume * musicGain;
        }

        private static AudioSource Source(Transform parent, string name, float volume, bool loop)
        {
            var source = PassWorld.Group(name, parent).gameObject.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.volume = volume;
            source.loop = loop;
            return source;
        }

        internal void Sleep()
        {
            theme.Stop();
            percussion.Stop();
            ambient.Stop();
            foley.Stop();
            result.Stop();
            loopsStarted = false;
        }

        internal void Reset(CombatRun run)
        {
            Array.Clear(phases, 0, phases.Length);
            Array.Clear(segments, 0, segments.Length);
            nextFootstep = 0;
            dodged = false;
            previousArrows = run.ArrowsRemaining;
            lastStatus = run.Status;
            tension = 0;
            foley.Stop();
            result.Stop();
        }

        internal void Cue(CombatCue cue, CombatRun run)
        {
            switch (cue.Result)
            {
                case ContactResult.Hit: Play(cue.IsArrow ? PassSoundId.ArrowHit : PassSoundId.Hit, .73f); break;
                case ContactResult.Guard: Play(PassSoundId.Guard, .87f); break;
                case ContactResult.Deflect:
                    Play(PassSoundId.Deflect, .85f);
                    if (run.View(cue.Attacker).Broken) Play(PassSoundId.Break, .65f);
                    break;
                case ContactResult.Break: Play(PassSoundId.Break, .96f); break;
                case ContactResult.Fall: Play(PassSoundId.Fall, .83f); break;
                case ContactResult.Finish: Play(PassSoundId.Finish, .98f); break;
            }
        }

        internal void Step(CombatRun run)
        {
            if (clips == null) return;
            if (run.ArrowsRemaining < previousArrows) Play(PassSoundId.Arrow, .79f);
            previousArrows = run.ArrowsRemaining;
            for (int actor = 0; actor < run.Count; actor++)
            {
                var view = run.View(actor);
                if (view.Phase != phases[actor] || view.Segment != segments[actor])
                {
                    if (actor == 0 && run.Weapon == PlayerWeapon.Bow)
                    {
                        if (view.Phase == AttackPhase.Windup) Play(PassSoundId.Draw, .77f);
                    }
                    else if (view.Phase == AttackPhase.Active) Play(Swing(run, actor), actor == 0 ? .53f : .65f);
                }
                phases[actor] = view.Phase;
                segments[actor] = view.Segment;
            }
            var player = run.View(0);
            if (player.Dodging && !dodged) Play(PassSoundId.Dodge, .68f);
            dodged = player.Dodging;
            if (player.Moving && !player.Dodging && run.Tick >= nextFootstep)
            {
                nextFootstep = run.Tick + 19;
                Play(PassSoundId.Footstep, .47f);
            }
        }

        private static PassSoundId Swing(CombatRun run, int actor)
        {
            if (actor == 0) return PassSoundId.Blade;
            if (run.Map == null) return run.Encounter == Encounter.Gate ? PassSoundId.HeavySwing : PassSoundId.Blade;
            var enemy = run.Encounter == Encounter.Courtyard ? run.Map.Patrol : run.Map.Boss;
            return enemy.Weapon switch
            {
                EnemyWeapon.Spear or EnemyWeapon.Poleaxe => PassSoundId.Pole,
                EnemyWeapon.ShieldMace or EnemyWeapon.Maul => PassSoundId.HeavySwing,
                _ => PassSoundId.Blade
            };
        }

        internal void Advance(CombatRun run, bool menu, float delta)
        {
            if (clips == null) return;
            if (!loopsStarted)
            {
                loopsStarted = true;
                ambient.Play();
                if (theme.clip != null)
                {
                    double start = AudioSettings.dspTime + .12;
                    theme.PlayScheduled(start);
                    percussion.PlayScheduled(start);
                }
            }
            var status = run.Status;
            if (!menu && status != lastStatus)
            {
                if (status == RunStatus.Proceed) Play(PassSoundId.Gate, .82f);
                if (status == RunStatus.Victory && victory != null) result.PlayOneShot(victory);
                if (status == RunStatus.Defeated && defeat != null) result.PlayOneShot(defeat);
                if (status == RunStatus.Paused) foley.Stop();
            }
            lastStatus = status;
            bool fighting = !menu && status == RunStatus.Fighting;
            bool boss = fighting && run.Encounter == Encounter.Gate;
            float target = fighting && run.AttackOwner != 0 ? (boss ? 1f : .68f) : boss ? .38f : 0;
            tension = Mathf.MoveTowards(tension, target, delta * (target > tension ? 1.6f : .35f));
            float music = musicVolume * musicGain * (status == RunStatus.Victory || status == RunStatus.Defeated ? .23f : menu || status == RunStatus.Paused ? .55f : 1);
            float blend = 1 - Mathf.Exp(-delta * 3.5f);
            theme.volume = Mathf.Lerp(theme.volume, music, blend);
            percussion.volume = Mathf.Lerp(percussion.volume, musicVolume * musicGain * tension, blend);
            ambient.volume = Mathf.Lerp(ambient.volume, ambientVolume * effectsGain * (menu || status == RunStatus.Paused ? .48f : 1), blend);
        }

        private void Play(PassSoundId sound, float gain)
        {
            if (clips == null) return;
            int index = (int)sound;
            var options = clips[index];
            foley.PlayOneShot(options[variants[index]], gain);
            variants[index] = (variants[index] + 1) % options.Length;
        }
    }
}
