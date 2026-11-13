using System.Collections;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using BlackWhale.Network;

namespace BlackWhale.Tests
{
    public sealed class UnityTestJson : ITestJson
    {
        public T Decode<T>(string json) { return JsonUtility.FromJson<T>(json); }
        public string Encode<T>(T value) { return JsonUtility.ToJson(value); }
    }
    public sealed class UnityNetworkTests
    {
        [Test] public void StockPatternsAndInformationWindows()
        { TestContext.WriteLine(string.Join("\n", StockPatternValidation.Run())); }
        [UnityTest] public IEnumerator NextATimingBotsAndTenTurns()
        {
            var task = NextAValidation.Run(new UnityWireCodec());
            while (!task.IsCompleted) yield return null;
            if (task.IsFaulted) throw task.Exception;
            Assert.That(task.Result.Length, Is.GreaterThanOrEqualTo(13));
            TestContext.WriteLine(string.Join("\n", task.Result));
        }
        private RoomValidation Suite()
        { return new RoomValidation(Path.GetDirectoryName(Application.dataPath), new UnityTestJson()); }
        [Test] public void RoleAFixturesAndFlow()
        {
            string[] checks = Suite().RunAll();
            Assert.That(checks.Length, Is.EqualTo(17));
            TestContext.WriteLine(string.Join("\n", checks));
        }
        [UnityTest] public IEnumerator FourSocketClientsUsingUnityJson()
        {
            var task = TransportValidation.Run(Suite(), new UnityWireCodec());
            while (!task.IsCompleted) yield return null;
            if (task.IsFaulted) throw task.Exception;
            Assert.That(task.Result.Length, Is.EqualTo(8));
            TestContext.WriteLine(string.Join("\n", task.Result));
        }
        [UnityTest] public IEnumerator RealBCUsingUnityJson()
        {
            var task = RealInformationValidation.Run(Suite(), new UnityWireCodec());
            while (!task.IsCompleted) yield return null;
            if (task.IsFaulted) throw task.Exception;
            Assert.That(task.Result.Length, Is.EqualTo(4));
            TestContext.WriteLine(string.Join("\n", task.Result));
        }
    }
}
