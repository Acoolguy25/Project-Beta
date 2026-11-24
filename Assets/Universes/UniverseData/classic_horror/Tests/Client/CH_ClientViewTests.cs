using System.Reflection;
using NUnit.Framework;
using RyanAssets.Client.ClientUI.Topbar;
using UnityEngine;
using Universes.UniverseData.classic_horror.Client;

namespace Universes.UniverseData.classic_horror.Tests {
    /// <summary>The night's client views: the shared health bar stays out of it, and hiding looks out through the door.</summary>
    public sealed class CH_ClientViewTests {
        GameObject cameraObject, viewObject, torchObject;

        [TearDown] public void TearDown() {
            ClientHealthbar.Suppressed = false;
            foreach (GameObject made in new[] { cameraObject, viewObject, torchObject }) if (made != null) Object.DestroyImmediate(made);
        }

        [Test] public void TheHealthBarIsHiddenWhileSuppressedAndBackWhenCleared() {
            Assert.IsTrue(ClientHealthbar.ShowsFor(true, false, 40), "A hurt character shows the bar.");
            ClientHealthbar.Suppressed = true;
            Assert.IsFalse(ClientHealthbar.ShowsFor(true, false, 40), "Suppressed (the Drowned Choir counts lives, not health): no bar.");
            ClientHealthbar.Suppressed = false;
            Assert.IsTrue(ClientHealthbar.ShowsFor(true, false, 40), "Cleared when the night ends: the bar is back for the next game.");
            Assert.IsFalse(ClientHealthbar.ShowsFor(true, true, 100), "At full health it stays hidden anyway.");
        }

        [Test] public void HidingLooksOutThroughTheDoorWithTheTorchDark() {
            cameraObject = new GameObject("Camera");
            var camera = cameraObject.AddComponent<Camera>();
            camera.nearClipPlane = 0.05f;
            torchObject = new GameObject("Flashlight");
            var beam = torchObject.AddComponent<Light>();
            viewObject = new GameObject("Hiding view");
            var view = viewObject.AddComponent<CH_HidingView>();
            Vector3 outward = Vector3.right;

            view.Enter(outward, torchObject, camera);
            Assert.Greater(camera.nearClipPlane, 0.2f, "The near plane moves past the door so the room shows through the crack.");
            // Looking straight back into the wardrobe, and far down: the view turns to peek out of the door instead.
            cameraObject.transform.rotation = Quaternion.Euler(70f, 270f, 0f);
            Hold(view);
            Vector3 looking = cameraObject.transform.forward;
            Assert.Greater(Vector3.Dot(new Vector3(looking.x, 0f, looking.z).normalized, outward), Mathf.Cos(30f * Mathf.Deg2Rad), "Still looking out of the door.");
            Assert.Less(Mathf.Abs(Mathf.DeltaAngle(0f, cameraObject.transform.eulerAngles.x)), 30f, "Looking up or down only a little.");
            Assert.IsFalse(beam.enabled, "The flashlight is dark inside.");

            view.Exit();
            Assert.AreEqual(0.05f, camera.nearClipPlane, 1e-5f, "Leaving gives the camera back as it was.");
            Assert.IsFalse(view.Active);
        }

        static void Hold(CH_HidingView view) =>
            typeof(CH_HidingView).GetMethod("LateUpdate", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(view, null);
    }
}
