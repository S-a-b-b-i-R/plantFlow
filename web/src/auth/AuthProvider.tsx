import { useEffect, useMemo, useState, type ReactNode } from "react";
import { useQueryClient } from "@tanstack/react-query";
import {
    GoogleAuthProvider,
    onAuthStateChanged,
    signInWithPopup,
    signOut as firebaseSignOut,
} from "firebase/auth";
import type { User } from "firebase/auth";
import { auth } from "@/lib/firebase";
import { AuthContext, type AuthValue } from "./auth-context";

export function AuthProvider({ children }: { children: ReactNode }) {
    const queryClient = useQueryClient();
    const [firebaseUser, setFirebaseUser] = useState<User | null | undefined>(
        undefined,
    );

    useEffect(() => onAuthStateChanged(auth, setFirebaseUser), []);

    const value = useMemo<AuthValue>(
        () => ({
            firebaseUser,
            signInWithGoogle: async () => {
                await signInWithPopup(auth, new GoogleAuthProvider());
            },
            signOut: async () => {
                await firebaseSignOut(auth);
                queryClient.clear();
            },
        }),
        [firebaseUser, queryClient],
    );

    return (
        <AuthContext.Provider value={value}>{children}</AuthContext.Provider>
    );
}
