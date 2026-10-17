namespace Hyperlite.Persistence.Bindings
{
    using Hyperlite.Persistence.Reflection;

    internal abstract class InspectedPropertyKeyBinding : PartialKeyBinding
    {
        protected InspectedPropertyKeyBinding(InspectedProperty inspectedProperty, IColumnBinding columnBinding)
            : base(inspectedProperty.InspectedType, columnBinding)
        {
        }
    }
}
