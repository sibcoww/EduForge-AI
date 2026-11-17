using EduForge.Api;using Xunit;
namespace EduForge.Tests;
public class PythonSandboxTests {
 [Fact][Trait("Category","Docker")]public async Task RunsInputWithoutHostFilesystemAccess(){var runner=new PythonRunner();Assert.Equal("5\n",await runner.Run("print(int(input())+2)","3",default));Assert.Equal("blocked\n",await runner.Run("try:\n open('/forbidden','w')\nexcept OSError:\n print('blocked')","",default));}
 [Fact][Trait("Category","Docker")]public async Task EnforcesDeadline(){var e=await Assert.ThrowsAsync<InvalidOperationException>(()=>new PythonRunner().Run("while True: pass","",default));Assert.Contains("8 секунд",e.Message);}
}
