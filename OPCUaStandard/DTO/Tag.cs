using System;
using Opc.Ua;

namespace OPCUaStandard.DTO
{
    public class Tag
    {
        /// <summary>
        /// Name of the tag
        /// </summary>
        public string Name
        {
            get
            {
                return this.Address.Substring(this.Address.LastIndexOf(".") + 1);
            }
        }

        /// <summary>
        /// Address of the tag
        /// </summary>
        public string Address { get; set; }

        /// <summary>
        /// Value of the tag
        /// </summary>
        public Object Value { get; set; }


        /// <summary>
        /// Status code of the tag
        /// </summary>
        public StatusCode Code { get; set; }

        /// <summary>
        /// Quality of the tag
        /// </summary>
        public Boolean Quality => StatusCode.IsGood(this.Code);
    }
}