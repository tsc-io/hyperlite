using System;

// Declared here, not as <CLSCompliant> in the project: that is not an SDK property, so it was
// ignored, Hyperlite was never marked compliant, and every Hyperlite type in the public api of
// the two compliant libraries above it raised CS3002 or CS3003 there.
[assembly: CLSCompliant(true)]
