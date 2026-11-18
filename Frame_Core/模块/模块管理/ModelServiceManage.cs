using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Frame_Core
{
    /// <summary>
    /// 模块服务管理
    /// </summary>
    public class ModelServiceManage
    {
        /// <summary>
        /// 模块字典
        /// </summary>
        public Dictionary<string, ModelControl> m_dicModels = new Dictionary<string, ModelControl>();

        /// <summary>
        /// 添加模块
        /// </summary>
        /// <param name="modelName"></param>
        /// <param name="model"></param>
        public void AddModel(string modelName, ModelControl model)
        {
            m_dicModels[modelName] = model;
            // 默认权限按模块分级（系统设置/工艺标准/数据分析对操作员开放，调试/外联关闭）
            m_dicModels[modelName].modelPrivilege = ModelPrivilege.DefaultFor(modelName);
        }

        /// <summary>
        /// 获取模块
        /// </summary>
        /// <param name="modelName"></param>
        /// <returns></returns>
        public ModelControl GetModel(string modelName)
        {
            bool result = m_dicModels.TryGetValue(modelName, out ModelControl model);
            if (result) return model;
            else return null;
        }
    }
}
