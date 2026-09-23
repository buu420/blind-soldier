import java.nio.file.Files;
import java.nio.file.Path;
import java.util.LinkedHashSet;
import ghidra.app.script.GhidraScript;
import ghidra.app.decompiler.DecompInterface;
import ghidra.program.model.listing.Function;

public class DumpGilEvidence extends GhidraScript {
 public void run() throws Exception {
  String[] args = getScriptArgs();
  var out = new StringBuilder("Program: " + currentProgram.getName() + "\n");
  var functions = new LinkedHashSet<Function>();
  for (int index=0; index<16; index++) out.append("MAIN DISPATCH ").append(index).append(" ").append(String.format("%08X", getInt(toAddr(0x0091AB98L + index * 4)))).append("\n");
  var refs = currentProgram.getReferenceManager().getReferencesTo(toAddr(0x00DC08B4L));
  while (refs.hasNext()) {
   var ref = refs.next();
   var fn = getFunctionContaining(ref.getFromAddress());
   out.append("GIL XREF ").append(ref.getFromAddress()).append(" ").append(ref.getReferenceType())
      .append(" ").append(fn == null ? "none" : fn.getName()).append(" ")
      .append(getInstructionContaining(ref.getFromAddress())).append("\n");
   if (args.length == 1 && fn != null && fn.getEntryPoint().getOffset() >= 0x006C0000L && fn.getEntryPoint().getOffset() < 0x00730000L) functions.add(fn);
  }
  for (int i=1; i<args.length; i++) {
   var address=toAddr(Long.parseLong(args[i],16));
   var fn=getFunctionAt(address);
   if (fn==null) fn=getFunctionContaining(address);
   if (fn==null) throw new IllegalStateException("Missing function " + address);
   functions.add(fn);
  }
  var dec = new DecompInterface();
  dec.openProgram(currentProgram);
  try {
   for (var fn : functions) {
    out.append("\nFUNCTION ").append(fn.getEntryPoint()).append(" ").append(fn.getName()).append("\n");
    for (var caller : fn.getCallingFunctions(monitor)) out.append("CALLER ").append(caller.getEntryPoint()).append("\n");
    var r=dec.decompileFunction(fn,60,monitor);
    if (!r.decompileCompleted() || r.getDecompiledFunction()==null) throw new IllegalStateException(r.getErrorMessage());
    out.append(r.getDecompiledFunction().getC());
   }
  } finally { dec.dispose(); }
  Files.writeString(Path.of(args[0]),out.toString());
  println("Wrote gil renderer evidence.");
 }
}
