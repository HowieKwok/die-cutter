using System.Collections.Generic;

namespace Frame_Core
{
    /// <summary>
    /// PLC 点位表的能力声明：谁拥有点位，谁说明"需要在服务端预先登记哪些地址"。
    /// 符号寻址（CIP）机型返回符号名；寄存器寻址（FINS 等）返回空集合。
    /// 这样仿真/服务端无需认识任何具体机型，新增机型只要实现本接口。
    /// </summary>
    public interface IPLCAddressTable
    {
        /// <summary>需要在符号型服务端预注册的地址；没有符号概念的机型返回空。</summary>
        IEnumerable<string> GetRegisterSymbols();
    }
}