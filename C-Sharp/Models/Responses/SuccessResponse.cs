using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace GSwap.Models.Responses
{
    /// <summary>
    /// This class simply sets IsSuccesful to true by default.
    /// Many of the response classes will end up inheriting from here
    /// since they should be returning IsSuccessful = true
    /// </summary>
    [Serializable]
    public class SuccessResponse :  BaseResponse
    {
        public SuccessResponse()
        {
            
            this.IsSuccessful = true;
        }
    }
}
