using Mono.Cecil;
using Mono.Cecil.Cil;
using System.Security.Cryptography;

if (args.Length != 2) throw new ArgumentException("input.exe output.exe");
string Hash(string path) { using var sha = SHA256.Create(); return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path))).Replace("-", ""); }
if (Hash(args[0]) != "48E7DB906FA30B99A828FEBE502F6BF0BE16BE3852D67CB78A6C0F43BF759BFD")
    throw new Exception("Unsupported executable. Expected Teevolution Terra Pro 1.0.0.43 with the documented SHA256.");
using var assembly = AssemblyDefinition.ReadAssembly(args[0]);
var module = assembly.MainModule;
var home = module.Types.Single(t => t.FullName == "Mouse_Drive_Beta.FormHomePage");
var main = module.Types.Single(t => t.FullName == "Mouse_Drive_Beta.Program").Methods.Single(m => m.Name == "Main");
if (home.Fields.Any(f => f.Name == "StartInTray")) throw new Exception("Already patched");
var field = new FieldDefinition("StartInTray", FieldAttributes.Assembly, module.TypeSystem.Boolean);
home.Fields.Add(field);
var setter = main.Body.Instructions.Single(i => i.Operand is MethodReference m && m.Name == "set_ShowInTaskbar");
// The original startup-only branch pushes the form and false before this call.
var il = main.Body.GetILProcessor();
var loadForm = setter.Previous.Previous;
if (setter.Previous.OpCode != OpCodes.Ldc_I4_0 || loadForm.OpCode.Code is not (Code.Ldloc_0 or Code.Ldloc_1 or Code.Ldloc_2 or Code.Ldloc_3 or Code.Ldloc_S or Code.Ldloc))
    throw new Exception("Unexpected startup IL");
Instruction copyLoad = loadForm.Operand is VariableDefinition v ? Instruction.Create(OpCodes.Ldloc, v) : Instruction.Create(loadForm.OpCode);
il.InsertAfter(setter, copyLoad);
var one = Instruction.Create(OpCodes.Ldc_I4_1);
il.InsertAfter(copyLoad, one);
il.InsertAfter(one, Instruction.Create(OpCodes.Stfld, field));

var formType = home.BaseType;
var setVisible = new MethodReference("SetVisibleCore", module.TypeSystem.Void, formType) { HasThis = true };
setVisible.Parameters.Add(new ParameterDefinition(module.TypeSystem.Boolean));
var getHandle = new MethodReference("get_Handle", module.TypeSystem.IntPtr, formType) { HasThis = true };
var method = new MethodDefinition("SetVisibleCore", MethodAttributes.Family | MethodAttributes.Virtual | MethodAttributes.HideBySig, module.TypeSystem.Void);
method.Parameters.Add(new ParameterDefinition("value", ParameterAttributes.None, module.TypeSystem.Boolean));
home.Methods.Add(method);
var p = method.Body.GetILProcessor();
var normal = Instruction.Create(OpCodes.Ldarg_0);
p.Emit(OpCodes.Ldarg_1);
p.Emit(OpCodes.Brfalse_S, normal);
p.Emit(OpCodes.Ldarg_0);
p.Emit(OpCodes.Ldfld, field);
p.Emit(OpCodes.Brfalse_S, normal);
p.Emit(OpCodes.Ldarg_0);
p.Emit(OpCodes.Ldc_I4_0);
p.Emit(OpCodes.Stfld, field);
// Create the native handle for background device processing without showing it.
p.Emit(OpCodes.Ldarg_0);
p.Emit(OpCodes.Call, getHandle);
p.Emit(OpCodes.Pop);
p.Emit(OpCodes.Ldc_I4_0);
p.Emit(OpCodes.Starg_S, method.Parameters[0]);
p.Append(normal);
p.Emit(OpCodes.Ldarg_1);
p.Emit(OpCodes.Call, setVisible);
p.Emit(OpCodes.Ret);

// Built-in Windows startup must use the same hidden-launch mode.
var reg = module.Types.Single(t => t.FullName == "Mouse_Drive_Beta.FileManager.RegeditManager");
var concat = new MethodReference("Concat", module.TypeSystem.String, module.TypeSystem.String) { HasThis = false };
concat.Parameters.Add(new ParameterDefinition(module.TypeSystem.String));
concat.Parameters.Add(new ParameterDefinition(module.TypeSystem.String));
foreach (var m in reg.Methods.Where(m => m.Name is "SetRunBoot" or "GetRunBoot")) {
    var body = m.Body.GetILProcessor();
    var first = m.Body.Instructions[0];
    var path = m.Parameters.Single(x => x.Name == "path");
    body.InsertBefore(first, Instruction.Create(OpCodes.Ldarg, path));
    body.InsertBefore(first, Instruction.Create(OpCodes.Ldstr, " -startup"));
    body.InsertBefore(first, Instruction.Create(OpCodes.Call, concat));
    body.InsertBefore(first, Instruction.Create(OpCodes.Starg, path));
}
// The window cross and the tray Exit originally share an exit handler.
// Rewire only the cross to the existing minimize handler; retain tray cleanup.
foreach (var window in new[] { home, module.Types.Single(t => t.FullName == "Mouse_Drive_Beta.FormMain") }) {
    var init = window.Methods.Single(m => m.Name == "InitializeComponent");
    var minimize = window.Methods.Single(m => m.Name == "customButton_Mini_Click");
    bool IsCross(Instruction i) {
        int count = 0;
        for (var previous = i.Previous; previous != null && count < 4; previous = previous.Previous, count++)
            if (previous.Operand is FieldReference f && f.Name == "customButton_Close") return true;
        return false;
    }
    var cross = init.Body.Instructions.Single(i => i.OpCode == OpCodes.Ldftn && IsCross(i));
    cross.Operand = minimize;
    var formsScope = window.BaseType.Scope;
    var closingArgs = new TypeReference("System.Windows.Forms", "FormClosingEventArgs", module, formsScope);
    var closeReason = new TypeReference("System.Windows.Forms", "CloseReason", module, formsScope, true);
    var getReason = new MethodReference("get_CloseReason", closeReason, closingArgs) { HasThis = true };
    var setCancel = new MethodReference("set_Cancel", module.TypeSystem.Void, closingArgs) { HasThis = true };
    setCancel.Parameters.Add(new ParameterDefinition(module.TypeSystem.Boolean));
    var baseClosing = new MethodReference("OnFormClosing", module.TypeSystem.Void, window.BaseType) { HasThis = true };
    baseClosing.Parameters.Add(new ParameterDefinition(closingArgs));
    var closing = new MethodDefinition("OnFormClosing", MethodAttributes.Family | MethodAttributes.Virtual | MethodAttributes.HideBySig, module.TypeSystem.Void);
    closing.Parameters.Add(new ParameterDefinition("e", ParameterAttributes.None, closingArgs));
    window.Methods.Add(closing);
    var c = closing.Body.GetILProcessor();
    var done = Instruction.Create(OpCodes.Ret);
    c.Emit(OpCodes.Ldarg_0); c.Emit(OpCodes.Ldarg_1); c.Emit(OpCodes.Call, baseClosing);
    c.Emit(OpCodes.Ldarg_1); c.Emit(OpCodes.Callvirt, getReason); c.Emit(OpCodes.Ldc_I4_3); c.Emit(OpCodes.Bne_Un_S, done);
    c.Emit(OpCodes.Ldarg_1); c.Emit(OpCodes.Ldc_I4_1); c.Emit(OpCodes.Callvirt, setCancel);
    c.Emit(OpCodes.Ldarg_0); c.Emit(OpCodes.Ldnull); c.Emit(OpCodes.Ldnull); c.Emit(OpCodes.Call, minimize);
    c.Append(done);
}
assembly.Write(args[1]);
using var check = AssemblyDefinition.ReadAssembly(args[1]);
var originalMethods = AssemblyDefinition.ReadAssembly(args[0]).MainModule.Types.SelectMany(t => t.Methods).ToArray();
var checkedMethods = check.MainModule.Types.SelectMany(t => t.Methods).ToArray();
bool Permitted(MethodDefinition m) =>
    (m.DeclaringType.FullName == "Mouse_Drive_Beta.Program" && m.Name == "Main") ||
    (m.DeclaringType.FullName == "Mouse_Drive_Beta.FileManager.RegeditManager" && (m.Name == "SetRunBoot" || m.Name == "GetRunBoot")) ||
    ((m.DeclaringType.FullName == home.FullName || m.DeclaringType.FullName == "Mouse_Drive_Beta.FormMain") && m.Name == "InitializeComponent");
foreach (var before in originalMethods.Where(m => m.HasBody && !Permitted(m))) {
    var after = checkedMethods.Single(m => m.FullName == before.FullName);
    string Describe(MethodDefinition m) => string.Join("\n", m.Body.Instructions.Select(i => i.ToString()));
    if (Describe(before) != Describe(after)) throw new Exception("Unexpected method change: " + before.FullName);
}
Console.WriteLine("Verified: all other original method bodies unchanged.");
Console.WriteLine("Input SHA256: " + Hash(args[0]));
Console.WriteLine("Output SHA256: " + Hash(args[1]));
