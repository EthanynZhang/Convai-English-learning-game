using System.Reflection;
using Convai.Scripts.Runtime.Features;
using Convai.Scripts.Runtime.UI;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests.EditMode
{
    public sealed class ConvaiCrosshairHandlerTests
    {
        [Test]
        public void AwakeIgnoresInteractableRecordsWithoutGameObjects()
        {
            GameObject cameraObject = new("Crosshair Test Camera");
            GameObject dataObject = new("Crosshair Test Data");
            GameObject validReference = new("Valid Reference");
            GameObject handlerObject = new("Crosshair Test Handler");
            handlerObject.SetActive(false);
            try
            {
                cameraObject.tag = "MainCamera";
                cameraObject.AddComponent<Camera>();
                ConvaiInteractablesData data =
                    dataObject.AddComponent<ConvaiInteractablesData>();
                data.Objects = new[]
                {
                    null,
                    new ConvaiInteractablesData.Object { Name = "Missing", gameObject = null },
                    new ConvaiInteractablesData.Object
                        { Name = "Valid", gameObject = validReference }
                };
                data.Characters = new[]
                {
                    null,
                    new ConvaiInteractablesData.Character
                        { Name = "Missing", gameObject = null }
                };

                ConvaiCrosshairHandler handler =
                    handlerObject.AddComponent<ConvaiCrosshairHandler>();
                MethodInfo awake = typeof(ConvaiCrosshairHandler).GetMethod(
                    "Awake", BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.IsNotNull(awake);
                Assert.DoesNotThrow(() => awake.Invoke(handler, null));
            }
            finally
            {
                Object.DestroyImmediate(handlerObject);
                Object.DestroyImmediate(validReference);
                Object.DestroyImmediate(dataObject);
                Object.DestroyImmediate(cameraObject);
            }
        }
    }
}
