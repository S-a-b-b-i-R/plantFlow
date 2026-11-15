import { createContext, useContext } from "react";
import type { User } from "firebase/auth";

export interface AuthValue {
    // undefined while Firebase works out whether someone is signed in. null when nobody is.
    firebaseUser: User | null | undefined;
    signInWithGoogle: () => Promise<void>;
    signOut: () => Promise<void>;
}

export const AuthContext = createContext<AuthValue | null>(null);

export function useAuth(): AuthValue {
    const value = useContext(AuthContext);
    if (!value) throw new Error("useAuth must be used inside AuthProvider.");
    return value;
}
