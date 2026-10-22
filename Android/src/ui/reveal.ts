import { createContext, useContext } from 'react';
import type { TextInput } from 'react-native';

/**
 * Provided by Screen: called by a text field when it gains focus, so the screen can scroll that
 * field - and room for the next field and the main button - above the keyboard. Sheets and dialogs
 * reset it to null; they rise above the keyboard on their own and must not scroll the page behind.
 */
export const RevealContext = createContext<((input: TextInput | null) => void) | null>(null);

export const useReveal = () => useContext(RevealContext);
