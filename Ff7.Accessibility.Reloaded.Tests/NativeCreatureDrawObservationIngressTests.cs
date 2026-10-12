using Ff7.Accessibility.Reloaded;

namespace Ff7.Accessibility.Reloaded.Tests;

internal static class NativeCreatureDrawObservationIngressTests
{
    public static void Run()
    {
        var order = new List<string>();
        var captures = 0; var validations = 0; var publishes = 0;
        var valid = true; var current = true;
        var expected = new DateTime(2026, 10, 12, 12, 0, 0, DateTimeKind.Utc);
        var ingress = new NativeCreatureDrawObservationIngress(
            (uint poly, uint owner, out FieldCreatureApproachSample sample) =>
            {
                order.Add("capture"); captures++; sample = default;
                Check(poly == 0x30000 && owner == 0x40000, "original draw arguments captured before forwarding");
                return valid;
            },
            sample => { order.Add("current"); validations++; return current; },
            (sample, utc) => { order.Add("publish"); publishes++; Check(utc == expected, "UTC observation stamp"); },
            () => expected);
        ingress.OnCallback(0x30000, 0x40000, (poly, owner) =>
        {
            order.Add("original"); Check(poly == 0x30000 && owner == 0x40000, "typed original receives exact arguments");
        });
        Check(order.SequenceEqual(new[] { "capture", "original", "current", "publish" }), "before/after original observation order");

        valid = false; order.Clear();
        ingress.OnCallback(0x30000, 0x40000, (poly, owner) => order.Add("original"));
        Check(order.SequenceEqual(new[] { "capture", "original" }) && publishes == 1, "invalid observation forwards without post-read or publication");
        valid = true; current = false; order.Clear();
        ingress.OnCallback(0x30000, 0x40000, (poly, owner) => order.Add("original"));
        Check(order.SequenceEqual(new[] { "capture", "original", "current" }) && publishes == 1, "changed native state defeats observation");

        var originalCalls = 0;
        var blind = new NativeCreatureDrawObservationIngress(
            (uint poly, uint owner, out FieldCreatureApproachSample sample) => throw new InvalidOperationException("read fault"),
            sample => throw new InvalidOperationException("must not re-read"), (sample, utc) => publishes++);
        blind.OnCallback(0x30000, 0x40000, (poly, owner) => originalCalls++);
        Check(originalCalls == 1 && blind.HasCaptureFault && !blind.HasOriginalFault, "capture exception preserves native draw exactly once");

        var faulted = new NativeCreatureDrawObservationIngress(
            (uint poly, uint owner, out FieldCreatureApproachSample sample) => { sample = default; return true; },
            sample => { validations++; return true; }, (sample, utc) => publishes++);
        var priorValidations = validations;
        faulted.OnCallback(0x30000, 0x40000, (poly, owner) => { originalCalls++; throw new InvalidOperationException("native failure"); });
        Check(originalCalls == 2 && faulted.HasOriginalFault && validations == priorValidations, "original failure stops without retry or post-read");

        var stoppedCaptures = captures;
        ingress.Stop(); ingress.Stop(); order.Clear();
        ingress.OnCallback(0x30000, 0x40000, (poly, owner) => order.Add("original"));
        Check(order.SequenceEqual(new[] { "original" }) && captures == stoppedCaptures, "stopped callback only forwards retained original");
        var retiringReads = 0; NativeCreatureDrawObservationIngress? retiring = null;
        retiring = new NativeCreatureDrawObservationIngress(
            (uint poly, uint owner, out FieldCreatureApproachSample sample) => { retiringReads++; sample = default; return true; },
            sample => { retiringReads++; return true; }, (sample, utc) => publishes++);
        var priorPublishes = publishes;
        retiring.OnCallback(0x30000, 0x40000, (poly, owner) => retiring.Stop());
        Check(retiringReads == 1 && publishes == priorPublishes, "retirement inside Original prevents further native reads and publication");

        var argumentReads = 0; originalCalls = 0;
        var translated = new NativeCreatureDrawObservationIngress(
            (uint poly, uint owner, out FieldCreatureApproachSample sample) => { sample = default; return poly == 0x30000 && owner == 0x40000; },
            sample => true, (sample, utc) => publishes++);
        translated.OnCallback((out uint poly, out uint owner) => { argumentReads++; poly = 0x30000; owner = 0x40000; return true; }, () => originalCalls++);
        Check(argumentReads == 1 && originalCalls == 1, "physical zero-argument host callback uses captured guest arguments");
        translated.OnCallback((out uint poly, out uint owner) => throw new InvalidOperationException("guest stack failure"), () => originalCalls++);
        Check(originalCalls == 2 && translated.HasCaptureFault, "guest argument read failure preserves native zero-argument original");
        translated.Stop();
        translated.OnCallback((out uint poly, out uint owner) => { argumentReads++; poly = owner = 0; return false; }, () => originalCalls++);
        Check(argumentReads == 1 && originalCalls == 3, "retired translated callback does not read guest ESP");
        var deliveryFault = new NativeCreatureDrawObservationIngress(
            (uint poly, uint owner, out FieldCreatureApproachSample sample) => { sample = default; return true; },
            sample => true, (sample, utc) => throw new InvalidOperationException("queue failure"));
        deliveryFault.OnCallback(0x30000, 0x40000, (poly, owner) => originalCalls++);
        Check(originalCalls == 4 && deliveryFault.HasCaptureFault && !deliveryFault.HasOriginalFault, "publication failure never retries native drawing");
        Console.WriteLine("Native creature ingress: original-once forwarding, argument capture, coherent publication and retirement passed.");
    }
    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
