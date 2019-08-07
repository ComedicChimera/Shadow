﻿namespace Whirlwind.Types
{
    class ReferenceType : DataType
    {
        public readonly DataType DataType;
        public readonly bool Owned;

        public ReferenceType(DataType dt, bool owned = false)
        {
            DataType = dt;
            Owned = owned;
        }

        protected override bool _equals(DataType other)
        {
            if (other.Classify() == TypeClassifier.REFERENCE)
                return DataType.Equals(((ReferenceType)other).DataType);

            return false;
        }

        protected override bool _coerce(DataType other)
        {
            if (other.Classify() == TypeClassifier.REFERENCE)
                return DataType.Coerce(((ReferenceType)other).DataType);

            return false;
        }

        public override TypeClassifier Classify() => TypeClassifier.REFERENCE;

        public override DataType ConstCopy()
            => new ReferenceType(DataType, Owned);
    }

    // self referential type
    class SelfType : DataType
    {
        private readonly string _name;

        // not readonly to allow late modification
        public DataType DataType;
        public bool Initialized = false;

        public SelfType(string name, DataType dt)
        {
            _name = name;
            DataType = dt;
        }

        protected override bool _equals(DataType other)
        {
            if (other is SelfType st && _name == st._name)
                return true;

            else if (DataType.Classify() == TypeClassifier.INTERFACE &&
                other.Classify() == TypeClassifier.INTERFACE_INSTANCE)
            {
                // allow for self types to not be problematic
                return ((InterfaceType)DataType).GetInstance().Equals(other);
            }

            return DataType.Equals(other);
        }

        protected override bool _coerce(DataType other)
        {
            if (other is SelfType st && _name == st._name)
                return true;

            else if (DataType.Classify() == TypeClassifier.INTERFACE &&
                other.Classify() == TypeClassifier.INTERFACE_INSTANCE)
            {
                // allow for self types to not be problematic
                return ((InterfaceType)DataType).GetInstance().Coerce(other);
            }

            return DataType.Coerce(other);
        }

        public override TypeClassifier Classify() => TypeClassifier.SELF;

        public override DataType ConstCopy()
            => new SelfType(_name, DataType) { Constant = true };
    }
}
