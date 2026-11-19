using Test.Domain.Mathematics;
using UnityEngine;

namespace Test.Client
{
    public abstract class ParticleLaunchProfile : ScriptableObject
    {
        public abstract ILaunchFormula2D CreateFormula();
    }
}
