using System;
using System.Collections.Generic;
using System.Linq;
using Hidano.FacialControl.Rec.Adapters.Playable;
using Hidano.FacialControl.Rec.Adapters.Playback;
using Hidano.FacialControl.Testing;
using NUnit.Framework;

namespace Hidano.FacialControl.Rec.Tests.EditMode
{
    [SmallTest]
    public sealed class ProductAssemblyIlScannerTests : SizedTestFixture
    {
        [Test]
        public void Scan_RecAdaptersAssembly_DetectsRecCharacterBindingToRecAnalogInjector()
        {
            var assembly = typeof(RecAnalogInjector).Assembly;
            var references = ProductAssemblyIlScanner.Scan(new[] { assembly });

            Assert.That(references.Any(reference =>
                reference.ReferrerType == typeof(RecCharacterBinding) &&
                reference.ReferencedType == typeof(RecAnalogInjector)), Is.True);
        }

        [Test]
        public void Scan_UnrelatedType_IsNotReported()
        {
            var references = ProductAssemblyIlScanner.Scan(new[] { typeof(RecAnalogInjector).Assembly });

            Assert.That(references.Any(reference =>
                reference.ReferrerType == typeof(RecAnalogInjector) &&
                reference.ReferencedType == typeof(ProductAssemblyIlScannerTests)), Is.False);
        }

        private sealed class GenericReferrer<T>
        {
            public RecAnalogInjector Create()
            {
                return new RecAnalogInjector(default);
            }
        }

        [Test]
        public void Scan_GenericMethodInTestFixture_ResolvesViaGenericContext()
        {
            var references = ProductAssemblyIlScanner.Scan(new[] { typeof(GenericReferrer<>).Assembly });

            Assert.That(references.Any(reference =>
                reference.ReferrerType == typeof(GenericReferrer<>) &&
                reference.ReferencedType == typeof(RecAnalogInjector)), Is.True);
        }

        [Test]
        public void Scan_ResolverThrows_FailsWithDeclaringTypeAndMethodName()
        {
            const string methodName = "ResolveFailure";
            var references = new List<ProductAssemblyIlReference>();
            Assert.That(() => ProductAssemblyIlScanner.ScanIlBytes(
                    typeof(ResolverFailureFixture), methodName, typeof(ResolverFailureFixture).Module,
                    null, null, new byte[] { 0x28, 0xFF, 0xFF, 0xFF, 0x7F }, references),
                Throws.TypeOf<InvalidOperationException>()
                    .With.Message.Contains(typeof(ResolverFailureFixture).FullName)
                    .And.Message.Contains(methodName));
        }

        [Test]
        public void Scan_UnknownOpcode_FailsWithMethodName()
        {
            var references = new List<ProductAssemblyIlReference>();
            Assert.That(() => ProductAssemblyIlScanner.ScanIlBytes(
                    typeof(ProductAssemblyIlScannerTests), nameof(Scan_UnknownOpcode_FailsWithMethodName),
                    typeof(ProductAssemblyIlScannerTests).Module, null, null, new byte[] { 0xFE, 0xFF }, references),
                Throws.TypeOf<InvalidOperationException>()
                    .With.Message.Contains(nameof(Scan_UnknownOpcode_FailsWithMethodName)));
        }

        private static class ResolverFailureFixture { }
    }
}
