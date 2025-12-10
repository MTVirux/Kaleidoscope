using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NightmareUI.OtterGuiWrapper.FileSystems.Generic;
public interface IFileSystemStorage
{
    public Guid GUID { get; set; }
    public string? GetCustomName();
    public void SetCustomName(string s);
}
