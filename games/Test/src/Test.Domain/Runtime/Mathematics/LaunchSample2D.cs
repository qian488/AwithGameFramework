using System.Numerics;

namespace Test.Domain.Mathematics
{
    public readonly struct LaunchSample2D
    {
        public LaunchSample2D(Vector2 position, Vector2 velocity)
        {
            Position = position;
            Velocity = velocity;
        }

        public Vector2 Position { get; }
        public Vector2 Velocity { get; }
    }

    public interface ILaunchFormula2D
    {
        LaunchSample2D Sample(float fraction, float speed);
    }
}
