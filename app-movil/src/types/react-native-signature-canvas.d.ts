declare module 'react-native-signature-canvas' {
  import { ComponentType, Ref } from 'react';

  export interface SignatureViewRef {
    readSignature: () => void;
    clearSignature: () => void;
  }

  export interface SignatureProps {
    ref?: Ref<SignatureViewRef>;
    onOK?: (signature: string) => void;
    onEmpty?: () => void;
    onClear?: () => void;
    onBegin?: () => void;
    onEnd?: () => void;
    descriptionText?: string;
    clearText?: string;
    confirmText?: string;
    webStyle?: string;
    autoClear?: boolean;
    imageType?: string;
    penColor?: string;
    backgroundColor?: string;
  }

  const Signature: ComponentType<SignatureProps>;
  export default Signature;
}
