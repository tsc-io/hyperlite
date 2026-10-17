namespace Hyperlite.Explorer
{
    using System.Windows;

    internal sealed class Transition : FrameworkElement
    {
        public static DependencyProperty FirstProperty;

        public static DependencyProperty SecondProperty;

        public static DependencyProperty SourceProperty;

        public static DependencyProperty StateProperty;

        /// <summary>
        /// Initializes static members of the <see cref="Transition"/> class.
        /// </summary>
        static Transition()
        {
            SourceProperty = DependencyProperty.Register("Source", typeof(object), typeof(Transition), new PropertyMetadata((obj, args) => ((Transition)obj).Swap()));
            FirstProperty = DependencyProperty.Register("First", typeof(object), typeof(Transition));
            SecondProperty = DependencyProperty.Register("Second", typeof(object), typeof(Transition));
            StateProperty = DependencyProperty.Register("State", typeof(TransitionState), typeof(Transition), new PropertyMetadata(TransitionState.First));
        }

        public enum TransitionState
        {
            First,

            Second
        }

        public object First
        {
            get
            {
                return this.GetValue(FirstProperty);
            }

            set
            {
                this.SetValue(FirstProperty, value);
            }
        }

        public object Second
        {
            get
            {
                return this.GetValue(SecondProperty);
            }

            set
            {
                this.SetValue(SecondProperty, value);
            }
        }

        public object Source
        {
            get
            {
                return this.GetValue(SourceProperty);
            }

            set
            {
                this.SetValue(SourceProperty, value);
            }
        }

        public TransitionState State
        {
            get
            {
                return (TransitionState)this.GetValue(StateProperty);
            }

            set
            {
                this.SetValue(StateProperty, value);
            }
        }

        private void Swap()
        {
            if (this.State == TransitionState.First)
            {
                this.Second = this.Source;
                this.State = TransitionState.Second;
            }
            else
            {
                this.First = this.Source;
                this.State = TransitionState.First;
            }
        }
    }
}
