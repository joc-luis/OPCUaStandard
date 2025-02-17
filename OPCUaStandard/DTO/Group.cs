using System.Collections.Generic;

namespace OPCUaStandard.DTO
{
    public class Group
    {
        /// <summary>
        /// Name of the group
        /// </summary>
        public string Name => this.Address.Substring(this.Address.LastIndexOf(".") + 1);

        /// <summary>
        /// Address of the group
        /// </summary>
        public string Address { get; set; }

        /// <summary>
        /// Groups into the group <see cref="Group"/>
        /// </summary>
        public List<Group> Groups { get; set; } = new List<Group>();

        /// <summary>
        /// Tags into the group <see cref="Tag"/>
        /// </summary>
        public List<Tag> Tags { get; set; } = new List<Tag>();
    }
}