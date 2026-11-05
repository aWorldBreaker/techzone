// Runner du phong cho `dotnet test`.
// Ly do: trong sandbox nay, kenh TCP giua vstest.console va testhost bi proxy
// can thiep (testhost nhan byte rac roi dut stream — xem log /tmp/vstest-diag2*),
// nen `dotnet test` abort truoc khi chay test nao, du code test dung.
// Runner nay goi TRUC TIEP cac method [Fact] cua TechZone.Tests (cung code,
// cung assertion xUnit) va bao pass/fail that. Tren may user / CI thi chay
// `dotnet test` binh thuong — khong can runner.
// Quet MOI class test trong assembly TechZone.Tests (khong hard-code ten class):
// moi class co it nhat 1 method [Fact] public, khoi tao bang ctor
// (ITestOutputHelper) neu co, neu khong thi ctor khong tham so.
using System.Reflection;
using Xunit;
using Xunit.Abstractions;
using TechZone.Tests;

var output = new ConsoleOutputHelper();
var assembly = typeof(MigrationTests).Assembly;
var testTypes = assembly.GetTypes()
    .Where(t => t.IsClass && !t.IsAbstract
        && t.GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Any(m => m.GetCustomAttribute<FactAttribute>() is not null))
    .OrderBy(t => t.FullName)
    .ToList();

var passed = 0;
var failed = 0;
var failedNames = new List<string>();
foreach (var testType in testTypes)
{
    var methods = testType.GetMethods(BindingFlags.Public | BindingFlags.Instance)
        .Where(m => m.GetCustomAttribute<FactAttribute>() is not null)
        .OrderBy(m => m.Name)
        .ToList();
    Console.WriteLine($"[runner] {methods.Count} test(s) trong {testType.FullName}");
    foreach (var method in methods)
    {
        Console.WriteLine($"[runner] RUN {testType.Name}.{method.Name} ...");
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            var instance = CreateInstance(testType, output);
            var result = method.Invoke(instance, null);
            if (result is Task task) await task;
            (instance as IDisposable)?.Dispose();
            Console.WriteLine($"[runner] PASS {testType.Name}.{method.Name} ({sw.Elapsed.TotalSeconds:0.0}s)");
            passed++;
        }
        catch (TargetInvocationException ex)
        {
            var inner = ex.InnerException ?? ex;
            Console.WriteLine($"[runner] FAIL {testType.Name}.{method.Name} ({sw.Elapsed.TotalSeconds:0.0}s): {inner.GetType().Name}: {inner.Message}");
            failed++;
            failedNames.Add($"{testType.Name}.{method.Name}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[runner] FAIL {testType.Name}.{method.Name} ({sw.Elapsed.TotalSeconds:0.0}s): {ex.GetType().Name}: {ex.Message}");
            failed++;
            failedNames.Add($"{testType.Name}.{method.Name}");
        }
    }
}
Console.WriteLine($"[runner] KET QUA: {passed} passed, {failed} failed, tong {passed + failed}");
if (failedNames.Count > 0)
    Console.WriteLine("[runner] FAILED: " + string.Join(", ", failedNames));
return failed == 0 ? 0 : 1;

static object CreateInstance(Type testType, ITestOutputHelper output)
{
    var ctorWithOutput = testType.GetConstructor(new[] { typeof(ITestOutputHelper) });
    if (ctorWithOutput is not null) return ctorWithOutput.Invoke(new object[] { output });
    var parameterless = testType.GetConstructor(Type.EmptyTypes);
    if (parameterless is not null) return Activator.CreateInstance(testType)!;
    throw new InvalidOperationException($"Khong khoi tao duoc {testType.FullName}: can ctor (ITestOutputHelper) hoac ctor rong.");
}

sealed class ConsoleOutputHelper : ITestOutputHelper
{
    public void WriteLine(string message) => Console.WriteLine("    [test] " + message);
    public void WriteLine(string format, params object[] args) => Console.WriteLine("    [test] " + string.Format(format, args));
}
