﻿namespace MegaMan.Common.Entities.Effects
{
    public class EntityFilterInfo
    {
        public string Type { get; set; }
        public Direction? Direction { get; set; }
        public PositionFilter Position { get; set; }

        public EntityFilterInfo Clone()
        {
            return new EntityFilterInfo {
                Type = Type,
                Direction = Direction,
                Position = Position.Clone()
            };
        }
    }
    
    public class PositionFilter
    {
        public RangeFilter X { get; set; }
        public RangeFilter Y { get; set; }

        public PositionFilter Clone()
        {
            return new PositionFilter {
                X = X.Clone(),
                Y = Y.Clone()
            };
        }
    }

    public class RangeFilter
    {
        public float? Min { get; set; }
        public float? Max { get; set; }

        public RangeFilter Clone()
        {
            return new RangeFilter { Max = Max, Min = Min };
        }
    }
}
