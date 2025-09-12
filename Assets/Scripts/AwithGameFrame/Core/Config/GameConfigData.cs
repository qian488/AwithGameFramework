using System;

namespace AwithGameFrame.Core.Config
{
    /// <summary>
    /// 游戏配置数据基类
    /// 所有游戏配置数据都应该继承此类
    /// </summary>
    [Serializable]
    public abstract class GameConfigData
    {
        /// <summary>
        /// 配置ID
        /// </summary>
        public int id;
        
        /// <summary>
        /// 配置名称
        /// </summary>
        public string name;
        
        /// <summary>
        /// 验证配置数据
        /// </summary>
        /// <returns>是否有效</returns>
        public virtual bool Validate()
        {
            return id > 0 && !string.IsNullOrEmpty(name);
        }
    }
    
    /// <summary>
    /// 角色配置数据示例
    /// </summary>
    [Serializable]
    public class CharacterConfig : GameConfigData
    {
        public float health;
        public float attack;
        public float speed;
        public float defense;
        
        public override bool Validate()
        {
            return base.Validate() && health > 0 && attack >= 0 && speed > 0 && defense >= 0;
        }
    }
    
    /// <summary>
    /// 武器配置数据示例
    /// </summary>
    [Serializable]
    public class WeaponConfig : GameConfigData
    {
        public int damage;
        public float attackSpeed;
        
        public override bool Validate()
        {
            return base.Validate() && damage > 0 && attackSpeed > 0;
        }
    }
}
