namespace MinimalRoslynCpg.Model;

public enum StableNodeRole
{
  None = 0,
  SyntaxNode = 1,
  SyntaxToken = 2,
  Operation = 3,
  Reference = 4,
  TypeReference = 5,
  Symbol = 6,
  TypeDeclaration = 7,
  Method = 8,
  MethodParameter = 9,
  MethodReturn = 10,
  MethodEntry = 11,
  MethodExit = 12,
  CallSite = 13,
  MemberAccess = 14,
  SyntaxTree = 15,
}
