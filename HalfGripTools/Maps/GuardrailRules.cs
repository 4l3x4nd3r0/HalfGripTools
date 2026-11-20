using System.Collections.Generic;
using UnityEngine;

namespace HalfGripTools
{
	/// <summary>The numbers of the guardrail rule. All speeds are the dash's number (game units a second times 1.8).</summary>
	public sealed class GuardrailTuning
	{
		/// <summary>Under this angle between the car's path and the rail it only scrapes: it slides, whatever the speed, and the rail does not bend.</summary>
		public float GlanceAngle = 12f;

		/// <summary>The least speed into the rail (the part of the speed that points at it) that bends it. Under it a touch is only a touch.</summary>
		public float BendMinInto = 15f;

		/// <summary>From this speed into the rail the touched pieces break, and the car pays for it like it does for felling a tree.</summary>
		public float BreakInto = 120f;

		/// <summary>What a bending hit costs the car, in dash units of speed.</summary>
		public float BendLossDash = 5f;

		/// <summary>How much of the speed into a bending rail comes back out (0 = a wall that kills it, 0.05 = barely any).</summary>
		public float Bounce = 0.05f;

		/// <summary>The share of its speed the car keeps when it breaks through (the game's own tree keeps 0.6).</summary>
		public float BreakKeep = 0.6f;
	}

	/// <summary>What a guardrail does with a hit.</summary>
	public enum GuardrailOutcome
	{
		/// <summary>Not moving into the rail.</summary>
		None,

		/// <summary>A scrape, or a touch too light to bend anything: the rail holds the car and nothing happens to either.</summary>
		Slide,

		/// <summary>The rail bows out and the car loses a little speed.</summary>
		Bend,

		/// <summary>The rail breaks; the car is slowed and takes crash damage.</summary>
		Break
	}

	/// <summary>The verdict on one hit, from <see cref="GuardrailRules.Judge"/>.</summary>
	public struct GuardrailVerdict
	{
		public GuardrailOutcome Outcome;

		/// <summary>The speed into the rail, in dash units.</summary>
		public float Into;

		/// <summary>The car's speed in dash units.</summary>
		public float Speed;

		/// <summary>The angle between the car's path and the rail, in degrees (90 is straight at it).</summary>
		public float Angle;

		/// <summary>How far to bow the touched pieces (0 to 1), for a bend.</summary>
		public float BendAmount;
	}

	/// <summary>
	/// How a guardrail treats a car, as speed and angle, shared by every map's rail (Highway, Temple of Drift, maps from files). Into = speed times
	/// the sine of the angle to the rail. Under <see cref="GuardrailTuning.GlanceAngle"/> the car slides along it, untouched. Above that, and
	/// into the rail faster than <see cref="GuardrailTuning.BendMinInto"/>, the rail bends and the car loses a few dash units. From
	/// <see cref="GuardrailTuning.BreakInto"/> it breaks, and the car loses speed and takes damage the way the game's tree does.
	/// </summary>
	public static class GuardrailRules
	{
		/// <summary>The number on the dash is the car's speed in game units a second times this (<c>SpeedDashboard.SpeedToDisplay</c>).</summary>
		public const float Dash = 1.8f;

		private static readonly Dictionary<int, float> lastBend = new Dictionary<int, float>();
		private static readonly Dictionary<int, float> lastBreak = new Dictionary<int, float>();

		/// <param name="velocity">The car's velocity in game units a second.</param>
		/// <param name="intoDash">The largest speed into a touched piece, in dash units: the velocity along the direction to the rail, times 1.8.</param>
		public static GuardrailVerdict Judge(Vector3 velocity, float intoDash, GuardrailTuning tuning)
		{
			Vector3 flat = new Vector3(velocity.x, 0f, velocity.z);
			float speed = flat.magnitude * Dash;
			GuardrailVerdict v = new GuardrailVerdict { Into = intoDash, Speed = speed };
			if (intoDash <= 0.5f || speed < 1f)
			{
				v.Outcome = GuardrailOutcome.None;
				return v;
			}
			v.Angle = Mathf.Asin(Mathf.Clamp01(intoDash / Mathf.Max(speed, intoDash))) * Mathf.Rad2Deg;
			if (intoDash >= tuning.BreakInto)
			{
				v.Outcome = GuardrailOutcome.Break;
			}
			else if (intoDash >= tuning.BendMinInto && v.Angle >= tuning.GlanceAngle)
			{
				v.Outcome = GuardrailOutcome.Bend;
				v.BendAmount = 0.15f + 0.55f * Mathf.Clamp01(intoDash / Mathf.Max(1f, tuning.BreakInto));
			}
			else
			{
				v.Outcome = GuardrailOutcome.Slide;
			}
			return v;
		}

		/// <summary>The velocity after a bending hit: a few dash units lighter, at most once every quarter second for a car.</summary>
		public static Vector3 AfterBend(CarControllerNew car, Vector3 velocity, GuardrailTuning tuning)
		{
			int id = car.GetInstanceID();
			if (lastBend.TryGetValue(id, out float at) && Time.time - at < 0.25f)
			{
				return velocity;
			}
			lastBend[id] = Time.time;
			float speed = velocity.magnitude;
			if (speed < 0.01f)
			{
				return velocity;
			}
			float lost = tuning.BendLossDash / Dash;
			return velocity * (Mathf.Max(0f, speed - lost) / speed);
		}

		/// <summary>
		/// The car's velocity after breaking through, and the crash damage it takes (the player only; an AI car is slowed but not hurt here). One
		/// barrier is one hit: a second break within half a second of the first costs nothing more.
		/// </summary>
		public static Vector3 AfterBreak(CarControllerNew car, Vector3 velocity, float intoDash, GuardrailTuning tuning, out float damage)
		{
			damage = 0f;
			int id = car.GetInstanceID();
			if (lastBreak.TryGetValue(id, out float at) && Time.time - at < 0.5f)
			{
				return velocity;
			}
			lastBreak[id] = Time.time;
			if (car.isPlayer && car.carLink != null)
			{
				damage = CrashDamage(car, intoDash / Dash);
				if (damage > 0f)
				{
					car.carLink.TakeCrashDamage(damage, true, DamageKind.Crash);
				}
			}
			return velocity * Mathf.Clamp01(tuning.BreakKeep);
		}

		/// <summary>
		/// The damage the game's own collision code (<c>CarControllerNew.ComputeCrashDamage</c>) gives a car that hits a wall, with a wall that stands
		/// straight up (a tree, a rail post), for a speed into it in game units a second. Read from the car's own crash settings, so it follows the game.
		/// </summary>
		public static float CrashDamage(CarControllerNew car, float intoUnitsPerSecond)
		{
			float minSpeed = car.crashMinImpactSpeed / Dash;
			float fullSpeed = car.crashFullDamageSpeed / Dash;
			if (car.rb == null || intoUnitsPerSecond <= minSpeed)
			{
				return 0f;
			}
			float mass = car.rb.mass;
			float energy = 0.5f * mass * intoUnitsPerSecond * intoUnitsPerSecond;
			float least = 0.5f * mass * minSpeed * minSpeed;
			float full = 0.5f * mass * fullSpeed * fullSpeed;
			float share = (energy - least) / Mathf.Max(1f, full);
			float damage = car.maxCrashDamage * (car.isPlayer && RunDifficulty.Hardcore ? Mathf.Max(0f, share) : Mathf.Clamp01(share));
			return damage < 1f ? 0f : damage;
		}

		/// <summary>The rule's table for a few hits, as text for the log: what each speed and angle does. Used to check the numbers without driving.</summary>
		public static string Table(GuardrailTuning tuning)
		{
			System.Text.StringBuilder text = new System.Text.StringBuilder("guardrail rule (dash speed / angle -> into, outcome):");
			foreach (float speed in new[] { 60f, 120f, 180f, 190f })
			{
				foreach (float angle in new[] { 5f, 10f, 15f, 20f, 45f, 90f })
				{
					float into = speed * Mathf.Sin(angle * Mathf.Deg2Rad);
					Vector3 velocity = Vector3.right * (speed / Dash);
					GuardrailVerdict verdict = Judge(velocity, into, tuning);
					text.Append("\n  ").Append(speed.ToString("0")).Append(" at ").Append(angle.ToString("0")).Append(" deg: into ").Append(into.ToString("0")).Append(" -> ").Append(verdict.Outcome);
				}
			}
			return text.ToString();
		}
	}
}
