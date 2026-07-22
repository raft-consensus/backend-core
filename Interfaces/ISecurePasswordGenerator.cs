namespace raft_backend.Interfaces;

public interface ISecurePasswordGenerator
{
    string Generate(int length);
}
